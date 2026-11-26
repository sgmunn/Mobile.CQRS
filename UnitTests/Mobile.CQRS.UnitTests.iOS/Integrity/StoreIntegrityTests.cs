using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Domain.UnitTests;
using Mobile.CQRS.Serialization;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.SQLite.Domain;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Integrity;

[TestFixture(false), TestFixture(true)]
public class StoreIntegrityTests
{
    private readonly bool sqlite;
    private SQLiteConnection connection;
    private string path;
    private IMergableEventStore store;
    public StoreIntegrityTests(bool sqlite) { this.sqlite = sqlite; }

    [SetUp]
    public void SetUp()
    {
        path = Path.Combine(Path.GetTempPath(), $"integrity-{Guid.NewGuid()}.db");
        if (sqlite)
        {
            connection = new SQLiteConnection(path);
            store = new EventStore(connection, TestEventSerializer.Instance);
        }
        else store = new InMemoryEventStore();
    }
    [TearDown]
    public void TearDown() { connection?.Dispose(); File.Delete(path); }

    [TestCase("gap"), TestCase("wrong-id"), TestCase("wrong-type"), TestCase("duplicate-id"), TestCase("empty-id")]
    public void InvalidBatchCannotPartiallyAppend(string kind)
    {
        var id = Guid.NewGuid();
        var first = EventIntegrityTests.Event(id, 1);
        var second = EventIntegrityTests.Event(id, 2);
        if (kind == "gap") second.Version = 3;
        if (kind == "wrong-id") second.AggregateId = Guid.NewGuid();
        if (kind == "wrong-type") second.AggregateType = "other";
        if (kind == "duplicate-id") second.Identity = first.Identity;
        if (kind == "empty-id") second.Identity = Guid.Empty;
        Assert.Throws<InvalidOperationException>(() => store.SaveEvents(id, new IAggregateEvent[] { first, second }, 0));
        Assert.AreEqual(0, store.GetCurrentVersion(id));
        Assert.IsEmpty(store.GetAllEvents(id));
        store.SaveEvents(id, new[] { first }, 0);
        Assert.AreEqual(1, store.GetCurrentVersion(id));
    }

    [Test]
    public void StaleAndEmptyAppendsCheckExpectedVersion()
    {
        var id = Guid.NewGuid();
        store.SaveEvents(id, new[] { EventIntegrityTests.Event(id, 1) }, 0);
        Assert.Throws<ConcurrencyException>(() => store.SaveEvents(id, Array.Empty<IAggregateEvent>(), 0));
        store.SaveEvents(id, Array.Empty<IAggregateEvent>(), 1);
        Assert.AreEqual(1, store.GetCurrentVersion(id));
    }

    [Test]
    public void IdentityCannotBeReusedInAnotherStreamOrRetainedPrefix()
    {
        var id = Guid.NewGuid();
        var first = EventIntegrityTests.Event(id, 1);
        store.SaveEvents(id, new[] { first }, 0);
        var second = EventIntegrityTests.Event(Guid.NewGuid(), 1);
        second.Identity = first.Identity;
        Assert.Throws<InvalidOperationException>(() => store.SaveEvents(second.AggregateId, new[] { second }, 0));
        var tail = EventIntegrityTests.Event(id, 2);
        tail.Identity = first.Identity;
        using var scope = sqlite ? new SqlUnitOfWorkScope(connection) : null;
        Assert.Throws<InvalidOperationException>(() => store.MergeEvents(id, new[] { tail }, 1, 1));
        Assert.AreEqual(1, store.GetCurrentVersion(id));
    }

    [Test]
    public void NestedPayloadsAreDetachedOnAppendAndEveryRead()
    {
        IMergableEventStore nested = sqlite ? new EventStore(connection, new NestedSerializer()) : new InMemoryEventStore();
        var id = Guid.NewGuid();
        var evt = new NestedEvent { AggregateId = id, Version = 1, Items = new List<int> { 1 } };
        nested.SaveEvents(id, new[] { evt }, 0);
        evt.Items[0] = 99;
        var read = (NestedEvent)nested.GetAllEvents(id).Single();
        CollectionAssert.AreEqual(new[] { 1 }, read.Items);
        read.Items.Add(2);
        CollectionAssert.AreEqual(new[] { 1 }, ((NestedEvent)nested.GetAllEvents(id).Single()).Items);
    }

    [Test]
    public void NativeConstraintFailureLeavesNoPartialBatchOrIndex()
    {
        if (!sqlite) return;
        var id = Guid.NewGuid();
        connection.Execute("create trigger FailSecond before insert on AggregateEvent when NEW.Version = 2 begin select raise(abort, 'injected failure'); end");
        Assert.Throws<SQLiteException>(() => store.SaveEvents(id,
            new[] { EventIntegrityTests.Event(id, 1), EventIntegrityTests.Event(id, 2) }, 0));
        Assert.IsEmpty(store.GetAllEvents(id));
        Assert.AreEqual(0, connection.ExecuteScalar<int>("select count(*) from AggregateIndex"));
        using var scope = new SqlUnitOfWorkScope(connection);
        Assert.Throws<SQLiteException>(() => store.SaveEvents(id,
            new[] { EventIntegrityTests.Event(id, 1), EventIntegrityTests.Event(id, 2) }, 0));
        Assert.Throws<InvalidOperationException>(() => scope.Commit());
    }

    [Test]
    public void ScopedStoreCannotAutoCommitAfterItsTransactionHasFailed()
    {
        if (!sqlite) return;
        var context = new EventSourcedDomainContext(path, TestEventSerializer.Instance);
        using var scope = context.BeginUnitOfWork();
        var scopedStore = scope.GetRegisteredObject<IEventStore>();
        var id = Guid.NewGuid();
        scopedStore.SaveEvents(id, new[] { EventIntegrityTests.Event(id, 1) }, 0);
        Assert.Throws<ConcurrencyException>(() => scopedStore.SaveEvents(id, Array.Empty<IAggregateEvent>(), 0));
        Assert.IsFalse(scope.GetRegisteredObject<SQLiteConnection>().IsInTransaction);
        var secondId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => scopedStore.SaveEvents(secondId,
            new[] { EventIntegrityTests.Event(secondId, 1) }, 0));
        Assert.Throws<InvalidOperationException>(() => scope.Commit());
        using var observer = new SQLiteConnection(path);
        Assert.AreEqual(0, observer.ExecuteScalar<int>("select count(*) from AggregateEvent"));
        Assert.AreEqual(0, observer.ExecuteScalar<int>("select count(*) from AggregateIndex"));
    }

    [Test]
    public void CorruptEnvelopeAndPayloadCannotBeReadEvenThroughAnEmptyRange()
    {
        if (!sqlite) return;
        var id = Guid.NewGuid();
        store.SaveEvents(id, new[] { EventIntegrityTests.Event(id, 1) }, 0);
        connection.Execute("update AggregateEvent set Version = 3");
        Assert.Throws<InvalidOperationException>(() => store.GetAllEvents(id));
        Assert.Throws<InvalidOperationException>(() => store.GetEventsAfterVersion(id, 100));
    }

    [Test]
    public void DatabaseEnforcesUniqueStreamPositionsAndDoesNotRepairLegacyDuplicatesSilently()
    {
        if (!sqlite) return;
        var id = Guid.NewGuid();
        store.SaveEvents(id, new[] { EventIntegrityTests.Event(id, 1) }, 0);
        var second = EventIntegrityTests.Event(id, 1);
        var row = new AggregateEvent { Identity = second.Identity, AggregateId = id, Version = 1,
            AggregateType = second.AggregateType, EventData = TestEventSerializer.Instance.SerializeToString(second) };
        Assert.Throws<SQLiteException>(() => connection.Insert(row));
        connection.Execute("drop index AggregateEvent_StreamVersion");
        connection.Insert(row);
        Assert.Throws<SQLiteException>(() => new EventStore(connection, TestEventSerializer.Instance));
        Assert.AreEqual(2, connection.ExecuteScalar<int>("select count(*) from AggregateEvent"));
    }

    [Test]
    public void RepositoryDistinguishesMissingRowsFromDatabaseErrors()
    {
        if (!sqlite) return;
        var repository = new SqlRepository<SqlRow>(connection);
        Assert.Throws<SQLiteException>(() => repository.GetById(Guid.NewGuid()));
        connection.CreateTable<SqlRow>();
        Assert.IsNull(repository.GetById(Guid.NewGuid()));
    }

    public sealed class SqlRow : IUniqueId { [PrimaryKey] public Guid Identity { get; set; } }

    public sealed class NestedEvent : EventBase { public List<int> Items { get; set; } = new(); }
    private sealed class NestedSerializer : ISerializer<IAggregateEvent>
    {
        public string SerializeToString(object value) => JsonSerializer.Serialize((NestedEvent)value);
        public IAggregateEvent DeserializeFromString(string data) => JsonSerializer.Deserialize<NestedEvent>(data);
    }
}
