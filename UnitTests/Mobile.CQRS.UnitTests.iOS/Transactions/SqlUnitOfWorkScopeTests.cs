using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Domain.UnitTests;
using Mobile.CQRS.Serialization;
using Mobile.CQRS.SQLite.Domain;
using System.Text.Json;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Transactions;

[TestFixture]
public class SqlUnitOfWorkScopeTests
{
    private string path;
    private SQLiteConnection connection;

    [SetUp]
    public void SetUp()
    {
        path = Path.Combine(Path.GetTempPath(), $"cqrs-{Guid.NewGuid()}.db");
        connection = new SQLiteConnection(path);
        connection.Execute("create table Probe (Id integer primary key)");
    }

    [TearDown]
    public void TearDown() { connection.Dispose(); File.Delete(path); }

    [TestCase(false)]
    [TestCase(true)]
    public void CommitOrRollbackIsDurableAfterReopening(bool commit)
    {
        using (var scope = new SqlUnitOfWorkScope(connection))
        {
            connection.Execute("insert into Probe values (1)");
            if (commit) scope.Commit();
        }
        using var reopened = new SQLiteConnection(path);
        Assert.AreEqual(commit ? 1 : 0, reopened.ExecuteScalar<int>("select count(*) from Probe"));
        AssertReleased();
    }

    [Test]
    public void ParticipantFailureRollsBackAndReleasesMonitor()
    {
        var scope = new SqlUnitOfWorkScope(connection);
        connection.Execute("insert into Probe values (1)");
        scope.Add(new UnitOfWorkScopeTests.Work { FailCommit = true });
        Assert.Throws<InvalidOperationException>(() => scope.Commit());
        scope.Dispose();
        Assert.AreEqual(0, connection.ExecuteScalar<int>("select count(*) from Probe"));
        AssertReleased();
        using var next = new SqlUnitOfWorkScope(connection);
        next.Commit();
    }

    [Test]
    public void DatabaseCommitFailureRemainsRollbackable()
    {
        connection.Execute("pragma foreign_keys = on");
        connection.Execute("create table Child (ParentId integer references Probe(Id) deferrable initially deferred)");
        var scope = new SqlUnitOfWorkScope(connection);
        connection.Execute("insert into Child values (99)");
        Assert.Throws<SQLiteException>(() => scope.Commit());
        Assert.IsTrue(connection.IsInTransaction);
        scope.Dispose();
        Assert.AreEqual(0, connection.ExecuteScalar<int>("select count(*) from Child"));
        AssertReleased();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedScopeIsRejectedWithoutEndingOuterTransaction(bool ownsConnection)
    {
        using (var outer = new SqlUnitOfWorkScope(connection))
        {
            connection.Execute("insert into Probe values (1)");
            Assert.Throws<InvalidOperationException>(() => new SqlUnitOfWorkScope(connection, ownsConnection));
            Assert.IsTrue(connection.IsInTransaction);
            outer.Commit();
        }
        AssertReleased();
        Assert.AreEqual(1, connection.ExecuteScalar<int>("select count(*) from Probe"));
    }

    [Test]
    public void FailedBeginReleasesMonitorAndResetsTransactionTracking()
    {
        // Raw BEGIN deliberately creates a native transaction unknown to the wrapper.
        connection.Execute("begin");
        try
        {
            Assert.Throws<SQLiteException>(() => new SqlUnitOfWorkScope(connection));
            Assert.IsFalse(Monitor.IsEntered(connection));
            Assert.IsFalse(connection.IsInTransaction);
        }
        finally { connection.Execute("rollback"); }
        using var next = new SqlUnitOfWorkScope(connection);
        next.Commit();
    }

    [Test]
    public void ThrowingParticipantDisposerStillRollsBack()
    {
        var scope = new SqlUnitOfWorkScope(connection);
        connection.Execute("insert into Probe values (1)");
        scope.Add(new UnitOfWorkScopeTests.Work { FailDispose = true });
        Assert.Throws<AggregateException>(() => scope.Dispose());
        Assert.AreEqual(0, connection.ExecuteScalar<int>("select count(*) from Probe"));
        AssertReleased();
    }

    [Test]
    public void FailedUnitOfWorkRollsBackEventsIndexAndSnapshotTogether()
    {
        var events = new EventStore(connection, TestEventSerializer.Instance);
        var snapshots = new SnapshotRepository(connection, new SnapshotSerializer());
        using (var scope = new SqlUnitOfWorkScope(connection))
        {
            var repository = new AggregateRepository(() => new TestAggregateRoot(), events, snapshots);
            var aggregate = new TestAggregateRoot { Identity = Guid.NewGuid() };
            aggregate.Execute(new TestCommand1 { AggregateId = aggregate.Identity, Value2 = 42 });
            repository.Save(aggregate);
            scope.Add(new UnitOfWorkScopeTests.Work { FailCommit = true });
            Assert.Throws<InvalidOperationException>(() => scope.Commit());
        }
        using var reopened = new SQLiteConnection(path);
        Assert.AreEqual(0, reopened.ExecuteScalar<int>("select count(*) from AggregateEvent"));
        Assert.AreEqual(0, reopened.ExecuteScalar<int>("select count(*) from AggregateIndex"));
        Assert.AreEqual(0, reopened.ExecuteScalar<int>("select count(*) from AggregateSnapshot"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ContextScopeClosesItsOwnedConnection(bool commit)
    {
        var context = new EventSourcedDomainContext(path, TestEventSerializer.Instance);
        var scope = context.BeginUnitOfWork();
        var owned = scope.GetRegisteredObject<SQLiteConnection>();
        if (commit) scope.Commit();
        scope.Dispose();
        Assert.AreEqual(IntPtr.Zero, owned.Handle);
        Assert.IsFalse(Monitor.IsEntered(owned));
        Assert.AreNotEqual(IntPtr.Zero, connection.Handle);
    }

    [Test]
    public void FailedConstructionClosesOwnedConnection()
    {
        using var owned = new SQLiteConnection(path);
        owned.Execute("begin");
        Assert.Throws<SQLiteException>(() => new SqlUnitOfWorkScope(owned, ownsConnection: true));
        Assert.AreEqual(IntPtr.Zero, owned.Handle);
        Assert.IsFalse(Monitor.IsEntered(owned));
    }

    private sealed class SnapshotSerializer : ISerializer<ISnapshot>
    {
        public string SerializeToString(object value) => JsonSerializer.Serialize((TestSnapshot)value);
        public ISnapshot DeserializeFromString(string value) => JsonSerializer.Deserialize<TestSnapshot>(value);
    }

    private void AssertReleased()
    {
        Assert.IsFalse(connection.IsInTransaction);
        Assert.IsFalse(Monitor.IsEntered(connection));
        Assert.IsTrue(Task.Run(() => {
            if (!Monitor.TryEnter(connection, TimeSpan.FromSeconds(1))) return false;
            Monitor.Exit(connection);
            return true;
        }).GetAwaiter().GetResult());
    }
}
