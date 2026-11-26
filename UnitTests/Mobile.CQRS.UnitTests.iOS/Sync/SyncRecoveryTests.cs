using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mobile.CQRS;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Domain.UnitTests;
using Mobile.CQRS.Serialization;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.SQLite.Domain;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Sync;

[TestFixture]
public class SyncRecoveryTests
{
    private string clientPath, remotePath, modelsPath;

    [SetUp]
    public void SetUp()
    {
        clientPath = Path.Combine(Path.GetTempPath(), $"sync-client-{Guid.NewGuid()}.db");
        remotePath = Path.Combine(Path.GetTempPath(), $"sync-remote-{Guid.NewGuid()}.db");
        modelsPath = Path.Combine(Path.GetTempPath(), $"sync-models-{Guid.NewGuid()}.db");
    }

    [TearDown]
    public void TearDown() { File.Delete(clientPath); File.Delete(remotePath); File.Delete(modelsPath); File.Delete(clientPath + ".crash-point"); }

    [Test]
    public void ReplacingHistoryInvalidatesSnapshotAtTheSameVersion()
    {
        var id = Guid.NewGuid();
        var context = Context(clientPath);
        Execute(context, id, 1);
        using (var scope = context.BeginUnitOfWork())
        {
            ((IMergableEventStore)scope.GetRegisteredObject<IEventStore>()).MergeEvents(id,
                new IAggregateEvent[] { Event(id, 1, 99) }, 1, 0);
            scope.Commit();
        }
        using var read = context.BeginUnitOfWork();
        Assert.IsNull(read.GetRegisteredObject<ISnapshotRepository>().GetById(id));
        var repository = new AggregateRepository(() => new TestAggregateRoot(),
            read.GetRegisteredObject<IEventStore>(), read.GetRegisteredObject<ISnapshotRepository>());
        Assert.AreEqual(99, ((TestAggregateRoot)repository.GetById(id)).Value2);
    }

    [Test]
    public void FirstSyncRejectsUnknownDivergentAncestry()
    {
        var context = Context(clientPath);
        var id = Guid.NewGuid();
        Execute(context, id, 1);
        var remote = new InMemoryEventStore();
        remote.SaveEvents(id, new IAggregateEvent[] { Event(id, 1, 99) }, 0);
        Assert.Throws<ConflictException>(() => context.SyncSomething<TestAggregateRoot>(remote, id));
    }


    [Test]
    public async Task InitialPushIsAcknowledgedAndAnotherClientCanImportIt()
    {
        var id = Guid.NewGuid();
        var client = Context(clientPath);
        var endpoint = Context(remotePath).AsSyncRemote();
        Execute(client, id, 1, 2);
        var original = History(client, id);
        var result = await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Assert.AreEqual(2, result.SyncedVersion);
        Assert.IsFalse(result.HistoryChanged);
        CollectionAssert.AreEqual(original, History(Context(remotePath), id));
        Assert.AreEqual(0, Pending(client, id));
        Assert.IsNull(Operation(client, id));
        Assert.AreEqual(2, Snapshot(client, id).Version);
        var importing = Context(clientPath + ".second");
        try
        {
            var download = await importing.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
            Assert.IsTrue(download.HistoryChanged);
            CollectionAssert.AreEqual(original, History(importing, id));
            Assert.AreEqual(0, Pending(importing, id));
        }
        finally { File.Delete(clientPath + ".second"); }
    }

    [Test]
    public async Task DivergentChangesRequirePolicyAndReplayCommandsInStoredOrder()
    {
        var id = Guid.NewGuid();
        var client = Context(clientPath);
        var server = Context(remotePath);
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id);
        Execute(client, id, 20, 30);
        Execute(server, id, 99);
        Assert.ThrowsAsync<ConflictException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id));
        Assert.AreEqual(2, Pending(client, id));
        Assert.IsNull(Operation(client, id));
        var result = await client.SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id, new AllowRebase());
        Assert.IsTrue(result.HistoryChanged);
        CollectionAssert.AreEqual(new[] { 1, 99, 20, 30 }, Values(client, id));
        CollectionAssert.AreEqual(History(client, id), History(server, id));
        Assert.IsNull(Snapshot(client, id));
        Assert.AreEqual(0, Pending(client, id));
        using var scope = client.BeginUnitOfWork();
        Assert.AreEqual(0, new ReadModelBuilderQueue(scope).Peek(1).Single().FromVersion);
        var repository = new AggregateRepository(() => new TestAggregateRoot(), scope.GetRegisteredObject<IEventStore>(), scope.GetRegisteredObject<ISnapshotRepository>());
        Assert.AreEqual(30, ((TestAggregateRoot)repository.GetById(id)).Value2);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnknownAppendOutcomeRetainsAndRetriesTheFrozenOperation(bool accepted)
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        var endpoint = server.AsSyncRemote();
        var failed = new HookRemote(endpoint, append: async batch => {
            Assert.AreEqual(batch.OperationId, Operation(client, id).Batch.OperationId);
            if (accepted) await endpoint.AppendAsync(batch);
            throw new TimeoutException("response lost");
        });
        Assert.ThrowsAsync<TimeoutException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(failed, id));
        var frozen = Operation(client, id);
        Assert.IsNotNull(frozen);
        Assert.AreEqual(1, Pending(client, id));
        Assert.AreEqual(0, State(client, id)?.LastSyncedVersion ?? 0);
        var result = await Context(clientPath).SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Assert.AreEqual(frozen.Batch.OperationId, result.OperationId);
        CollectionAssert.AreEqual(frozen.CanonicalHistory, History(server, id));
        Assert.AreEqual(1, Receipts());
        Assert.IsNull(Operation(client, id));
    }

    [Test]
    public async Task RebasedEventsAreNotRegeneratedAfterAnAcknowledgementIsLost()
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id);
        Execute(client, id, 20);
        Execute(server, id, 99);
        var policy = new AllowRebase();
        var endpoint = server.AsSyncRemote();
        Assert.ThrowsAsync<TimeoutException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(
            new HookRemote(endpoint, append: async batch => { await endpoint.AppendAsync(batch); throw new TimeoutException(); }), id, policy));
        var frozen = Operation(client, id);
        var result = await Context(clientPath).SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Assert.AreEqual(1, policy.Calls);
        Assert.AreEqual(frozen.Batch.OperationId, result.OperationId);
        CollectionAssert.AreEqual(frozen.CanonicalHistory, History(client, id));
        Assert.IsNull(Snapshot(client, id));
        Assert.AreEqual(0, Pending(client, id));
    }

    [Test]
    public async Task RemoteHeadRaceReplansOnlyAfterDefiniteRejection()
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var endpoint = server.AsSyncRemote();
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Execute(client, id, 20);
        var requests = new List<Guid>();
        var race = new HookRemote(endpoint, append: async batch => {
            requests.Add(batch.OperationId);
            if (requests.Count == 1) Execute(server, id, 99);
            await endpoint.AppendAsync(batch);
        });
        await client.SyncWithRemoteAsync<TestAggregateRoot>(race, id, new AllowRebase());
        Assert.AreEqual(2, requests.Distinct().Count());
        CollectionAssert.AreEqual(new[] { 1, 99, 20 }, Values(client, id));
        CollectionAssert.AreEqual(History(client, id), History(server, id));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LocalCommandsDuringTheRequestRemainPending(bool rebase)
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var endpoint = server.AsSyncRemote();
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Execute(client, id, 20);
        if (rebase) Execute(server, id, 99);
        var duringRequest = new HookRemote(endpoint, append: async batch => {
            await endpoint.AppendAsync(batch);
            Execute(client, id, 30); // proves no local SQLite transaction spans remote I/O
        });
        await client.SyncWithRemoteAsync<TestAggregateRoot>(duringRequest, id, new AllowRebase());
        Assert.AreEqual(1, Pending(client, id));
        Assert.AreEqual(30, Values(client, id).Last());
        Assert.AreEqual(rebase ? 4 : 3, History(client, id).Length);
        Assert.AreEqual(rebase ? 3 : 2, State(client, id).LastSyncedVersion);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        CollectionAssert.AreEqual(History(client, id), History(server, id));
        Assert.AreEqual(0, Pending(client, id));
    }

    [Test]
    public async Task ReceiptRejectsChangedPayloadAndSurvivesLaterRemoteAppends()
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        var endpoint = server.AsSyncRemote();
        Assert.ThrowsAsync<TimeoutException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(
            new HookRemote(endpoint, append: async batch => { await endpoint.AppendAsync(batch); throw new TimeoutException(); }), id));
        var operation = Operation(client, id);
        Execute(server, id, 99);
        await endpoint.AppendAsync(operation.Batch);
        var altered = operation.Batch with { ExpectedVersion = 1, Events = Array.Empty<SyncEvent>() };
        Assert.ThrowsAsync<ConflictException>(() => endpoint.AppendAsync(altered));
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Assert.AreEqual(1, State(client, id).LastSyncedVersion);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        CollectionAssert.AreEqual(History(server, id), History(client, id));
    }

    [Test]
    public void InvalidationRollsBackWithHistoryAndOutbox()
    {
        var client = Context(clientPath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        Guid delivery;
        using (var scope = client.BeginUnitOfWork()) delivery = new ReadModelBuilderQueue(scope).Peek(1).Single().DeliveryId;
        using (var scope = client.BeginUnitOfWork())
        {
            ((IMergableEventStore)scope.GetRegisteredObject<IEventStore>()).MergeEvents(id, new IAggregateEvent[] { Event(id, 1, 99) }, 1, 0);
            Assert.IsNull(scope.GetRegisteredObject<ISnapshotRepository>().GetById(id));
        }
        Assert.AreEqual(1, Snapshot(client, id).Version);
        CollectionAssert.AreEqual(new[] { 1 }, Values(client, id));
        using var read = client.BeginUnitOfWork();
        Assert.AreEqual(delivery, new ReadModelBuilderQueue(read).Peek(1).Single().DeliveryId);
    }

    [Test]
    public void ReplacementInvalidatesSnapshotsEvenWhenTheirSerializerIsNotConfigured()
    {
        var client = Context(clientPath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        client.SnapshotSerializer = null;
        using (var scope = client.BeginUnitOfWork())
        {
            ((IMergableEventStore)scope.GetRegisteredObject<IEventStore>()).MergeEvents(id, new IAggregateEvent[] { Event(id, 1, 99) }, 1, 0);
            scope.Commit();
        }
        Assert.IsNull(Snapshot(Context(clientPath), id));
    }

    [Test]
    public async Task ChangedRemoteIdentityCannotReceiveAPendingOperation()
    {
        var client = Context(clientPath);
        var endpoint = Context(remotePath).AsSyncRemote("canonical");
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        Assert.ThrowsAsync<TimeoutException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(
            new HookRemote(endpoint, append: _ => throw new TimeoutException()), id));
        var other = Context(remotePath).AsSyncRemote("other");
        Assert.ThrowsAsync<ConflictException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(other, id));
        Assert.IsNotNull(Operation(client, id));
        Assert.AreEqual(0, History(Context(remotePath), id).Length);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Assert.ThrowsAsync<ConflictException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(other, id));
    }


    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationAfterPreparationRetainsTheSameOperation(bool accepted)
    {
        var client = Context(clientPath);
        var endpoint = Context(remotePath).AsSyncRemote();
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        using var cancel = new CancellationTokenSource();
        var cancelling = new HookRemote(endpoint, append: async batch => {
            if (accepted) await endpoint.AppendAsync(batch);
            cancel.Cancel();
            cancel.Token.ThrowIfCancellationRequested();
        });
        Assert.ThrowsAsync<OperationCanceledException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(cancelling, id, cancellationToken: cancel.Token));
        var operation = Operation(client, id);
        Assert.IsNotNull(operation);
        Assert.AreEqual(1, Pending(client, id));
        var result = await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Assert.AreEqual(operation.Batch.OperationId, result.OperationId);
        Assert.AreEqual(1, Receipts());
    }

    [Test]
    public async Task LocalFinalizationFailureRollsBackSnapshotHistoryStateAndCommandAcknowledgements()
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var endpoint = server.AsSyncRemote();
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Execute(server, id, 99);
        Execute(client, id, 20);
        var before = History(client, id);
        var scopes = 0;
        var agent = new SyncAgent(() => {
            var scope = client.BeginUnitOfWork();
            if (++scopes == 3) scope.Add(new ActionWork(() => throw new InvalidOperationException("local commit failure")));
            return scope;
        }, endpoint, TestEventSerializer.Instance);
        Assert.ThrowsAsync<InvalidOperationException>(() => agent.SyncWithRemoteAsync<TestAggregateRoot>(id, new AllowRebase()));
        CollectionAssert.AreEqual(before, History(client, id));
        Assert.AreEqual(1, State(client, id).LastSyncedVersion);
        Assert.AreEqual(1, Pending(client, id));
        Assert.IsNotNull(Snapshot(client, id));
        var frozen = Operation(client, id);
        Assert.IsNotNull(frozen);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        CollectionAssert.AreEqual(frozen.CanonicalHistory, History(client, id));
        Assert.IsNull(Snapshot(client, id));
        Assert.AreEqual(0, Pending(client, id));
        Assert.AreEqual(2, Receipts());
    }

    [Test]
    public async Task AcknowledgedHistoryCannotBeRewrittenByTheRemote()
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id);
        using (var scope = server.BeginUnitOfWork())
        {
            ((IMergableEventStore)scope.GetRegisteredObject<IEventStore>()).MergeEvents(id, new IAggregateEvent[] { Event(id, 1, 99) }, 1, 0);
            scope.Commit();
        }
        Execute(client, id, 20);
        Assert.ThrowsAsync<ConflictException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id, new AllowRebase()));
        CollectionAssert.AreEqual(new[] { 1, 20 }, Values(client, id));
        Assert.AreEqual(1, Pending(client, id));
        Assert.IsNull(Operation(client, id));
    }

    [Test]
    public void UnsupportedSnapshotIsDiscardedForReplay()
    {
        var client = Context(clientPath);
        var id = Guid.NewGuid();
        Execute(client, id, 1, 2);
        using var scope = client.BeginUnitOfWork();
        var events = scope.GetRegisteredObject<IEventStore>();
        var snapshots = new InMemorySnapshotRepository<OtherSnapshot>();
        snapshots.Save(new OtherSnapshot { Identity = id, Version = 2 });
        var repository = new AggregateRepository(() => new TestAggregateRoot(), events, snapshots);
        Assert.AreEqual(2, ((TestAggregateRoot)repository.GetById(id)).Value2);
        var stateOnly = new AggregateRepository(() => new TestAggregateRoot(), snapshots);
        Assert.Throws<UnsupportedSnapshotException>(() => stateOnly.GetById(id));
    }

    [Test]
    public void TruncatingAllHistoryInvalidatesSnapshotsAndAllowsRecreation()
    {
        var client = Context(clientPath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        using (var scope = client.BeginUnitOfWork())
        {
            ((IMergableEventStore)scope.GetRegisteredObject<IEventStore>()).MergeEvents(id, Array.Empty<IAggregateEvent>(), 1, 0);
            scope.Commit();
        }
        Assert.IsNull(Snapshot(client, id));
        Assert.AreEqual(0, History(client, id).Length);
        Execute(client, id, 2);
        CollectionAssert.AreEqual(new[] { 2 }, Values(client, id));
    }

    [TestCase("before-send")]
    [TestCase("after-accept")]
    [TestCase("during-finalize")]
    public async Task ProcessExitRecoversFrozenSyncAndInvalidatesSnapshots(string phase)
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id);
        Execute(server, id, 99);
        Execute(client, id, 20);
        var before = History(client, id);
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(SyncRecoveryTests).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName=Mobile.CQRS.UnitTests.Sync.SyncRecoveryTests.CrashSyncProbe");
        start.Environment["CQRS_SYNC_CLIENT"] = clientPath;
        start.Environment["CQRS_SYNC_REMOTE"] = remotePath;
        start.Environment["CQRS_SYNC_MODELS"] = modelsPath;
        start.Environment["CQRS_SYNC_ID"] = id.ToString();
        start.Environment["CQRS_SYNC_PHASE"] = phase;
        using var child = System.Diagnostics.Process.Start(start);
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        Assert.AreNotEqual(0, child.ExitCode, await output + await error);
        Assert.AreEqual(phase, File.ReadAllText(clientPath + ".crash-point"));
        var frozen = Operation(client, id);
        Assert.IsNotNull(frozen);
        CollectionAssert.AreEqual(before, History(client, id));
        Assert.AreEqual(1, State(client, id).LastSyncedVersion);
        Assert.AreEqual(1, Pending(client, id));
        Assert.IsNotNull(Snapshot(client, id));
        Assert.AreEqual(phase == "before-send" ? 2 : 3, History(server, id).Length);
        var result = await Context(clientPath).SyncWithRemoteAsync<TestAggregateRoot>(server.AsSyncRemote(), id);
        Assert.AreEqual(frozen.Batch.OperationId, result.OperationId);
        CollectionAssert.AreEqual(frozen.CanonicalHistory, History(client, id));
        Assert.AreEqual(2, Receipts());
        Assert.IsNull(Snapshot(client, id));
        Assert.AreEqual(0, Pending(client, id));
    }

    [Test, Explicit("Only invoked by the sync crash-recovery subprocess test")]
    public void CrashSyncProbe()
    {
        clientPath = Environment.GetEnvironmentVariable("CQRS_SYNC_CLIENT");
        remotePath = Environment.GetEnvironmentVariable("CQRS_SYNC_REMOTE");
        modelsPath = Environment.GetEnvironmentVariable("CQRS_SYNC_MODELS");
        var id = Guid.Parse(Environment.GetEnvironmentVariable("CQRS_SYNC_ID"));
        var phase = Environment.GetEnvironmentVariable("CQRS_SYNC_PHASE");
        var client = Context(clientPath);
        var endpoint = Context(remotePath).AsSyncRemote();
        Action crash = () => { File.WriteAllText(clientPath + ".crash-point", phase); Environment.Exit(73); };
        var wrapper = new HookRemote(endpoint, async batch => {
            if (phase == "before-send") crash();
            await endpoint.AppendAsync(batch);
            if (phase == "after-accept") crash();
        });
        var scopes = 0;
        var agent = new SyncAgent(() => {
            var scope = client.BeginUnitOfWork();
            if (++scopes == 3 && phase == "during-finalize") scope.Add(new ActionWork(crash));
            return scope;
        }, wrapper, TestEventSerializer.Instance);
        agent.SyncWithRemoteAsync<TestAggregateRoot>(id, new AllowRebase()).GetAwaiter().GetResult();
        Assert.Fail("Expected process exit at the configured sync boundary.");
    }

    private sealed class OtherSnapshot : ISnapshot
    {
        public Guid Identity { get; set; }
        public int Version { get; set; }
    }
    private sealed class ActionWork : IUnitOfWork
    {
        private readonly Action action;
        public ActionWork(Action action) { this.action = action; }
        public void Commit() => action();
        public void Dispose() { }
    }


    [Test]
    public async Task LateConflictPreservesAcceptedOperationAndAllLocalCommandsForRecovery()
    {
        var client = Context(clientPath);
        var server = Context(remotePath);
        var endpoint = server.AsSyncRemote();
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id);
        Execute(client, id, 20);
        Execute(server, id, 99);
        var policy = new FirstRebaseOnly();
        var remote = new HookRemote(endpoint, async batch => {
            await endpoint.AppendAsync(batch);
            Execute(client, id, 30);
        });
        Assert.ThrowsAsync<ConflictException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(remote, id, policy));
        var operation = Operation(client, id);
        Assert.IsNotNull(operation);
        Assert.AreEqual(2, Pending(client, id));
        Assert.AreEqual(1, State(client, id).LastSyncedVersion);
        CollectionAssert.AreEqual(new[] { 1, 20, 30 }, Values(client, id));
        await client.SyncWithRemoteAsync<TestAggregateRoot>(endpoint, id, new AllowRebase());
        Assert.AreEqual(1, Pending(client, id));
        CollectionAssert.AreEqual(new[] { 1, 99, 20, 30 }, Values(client, id));
        Assert.IsNull(Snapshot(client, id));
        Assert.AreEqual(2, Receipts());
    }

    [Test]
    public void InvalidRemoteHistoryCannotCreateAPreparedOperation()
    {
        var client = Context(clientPath);
        var id = Guid.NewGuid();
        Execute(client, id, 1);
        var bad = new InvalidHistoryRemote(id);
        Assert.ThrowsAsync<InvalidOperationException>(() => client.SyncWithRemoteAsync<TestAggregateRoot>(bad, id));
        Assert.IsNull(Operation(client, id));
        Assert.AreEqual(1, Pending(client, id));
        CollectionAssert.AreEqual(new[] { 1 }, Values(client, id));
    }

    [Test]
    public async Task RemoteRejectsNonContiguousBatchWithoutWritingEvents()
    {
        var endpoint = Context(remotePath).AsSyncRemote();
        var id = Guid.NewGuid();
        var data = Event(id, 2, 99);
        var batch = new SyncBatch(Guid.NewGuid(), id, data.AggregateType, 0,
            SyncProtocol.Encode(new[] { data }, TestEventSerializer.Instance));
        Assert.ThrowsAsync<InvalidOperationException>(() => endpoint.AppendAsync(batch));
        Assert.AreEqual(0, (await endpoint.ReadAsync(id)).Events.Length);
    }

    private sealed class FirstRebaseOnly : ISyncRebasePolicy
    {
        private int calls;
        public bool CanRebase(IList<IAggregateEvent> local, IList<IAggregateEvent> remote) => ++calls == 1;
    }
    private sealed class InvalidHistoryRemote : ISyncRemote
    {
        private readonly Guid id;
        public InvalidHistoryRemote(Guid id) { this.id = id; }
        public string RemoteId => "invalid-history";
        public Task<SyncStream> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            var evt = Event(id, 2, 99);
            return Task.FromResult(new SyncStream(id, evt.AggregateType, SyncProtocol.Encode(new[] { evt }, TestEventSerializer.Instance)));
        }
        public Task AppendAsync(SyncBatch batch, CancellationToken cancellationToken = default) => throw new Exception("Invalid history must not be appended.");
    }


    [TestCase(false)]
    [TestCase(true)]
    public async Task RemoteCannotChangeTheTypeOfAnExistingStream(bool legacy)
    {
        var server = Context(remotePath);
        var endpoint = server.AsSyncRemote();
        var id = Guid.NewGuid();
        var first = Event(id, 1, 1);
        var seed = new SyncBatch(Guid.NewGuid(), id, first.AggregateType, 0,
            SyncProtocol.Encode(new[] { first }, TestEventSerializer.Instance));
        await endpoint.AppendAsync(seed);
        var before = History(server, id);
        var receiptCount = Receipts();
        var other = Event(id, 2, 99);
        other.AggregateType = "another-aggregate-type";
        var batch = new SyncBatch(Guid.NewGuid(), id, other.AggregateType, 1,
            SyncProtocol.Encode(new[] { other }, TestEventSerializer.Instance));
        if (legacy)
        {
            var store = new InMemoryEventStore();
            store.SaveEvents(id, new[] { first }, 0);
            var adapter = new EventStoreSyncRemote(store, TestEventSerializer.Instance, "legacy-type-check");
            Assert.ThrowsAsync<ConflictException>(() => adapter.AppendAsync(batch));
            CollectionAssert.AreEqual(before, SyncProtocol.Encode(store.GetAllEvents(id), TestEventSerializer.Instance));
        }
        else Assert.ThrowsAsync<ConflictException>(() => endpoint.AppendAsync(batch));
        CollectionAssert.AreEqual(before, History(server, id));
        Assert.AreEqual(receiptCount, Receipts());
    }

    private static SyncEvent[] History(EventSourcedDomainContext context, Guid id)
    {
        using var scope = context.BeginUnitOfWork();
        return SyncProtocol.Encode(scope.GetRegisteredObject<IEventStore>().GetAllEvents(id), TestEventSerializer.Instance);
    }
    private static int[] Values(EventSourcedDomainContext context, Guid id)
        => History(context, id).Select(x => ((TestEvent1)TestEventSerializer.Instance.DeserializeFromString(x.Data)).Value2).ToArray();
    private static int Pending(EventSourcedDomainContext context, Guid id)
    {
        using var scope = context.BeginUnitOfWork();
        return scope.GetRegisteredObject<IPendingCommandRepository>().PendingCommandsForAggregate(id).Count;
    }
    private static SyncOperation Operation(EventSourcedDomainContext context, Guid id)
    {
        using var scope = context.BeginUnitOfWork();
        return scope.GetRegisteredObject<ISyncOperationRepository>().Get(id);
    }
    private static ISyncState State(EventSourcedDomainContext context, Guid id)
    {
        using var scope = context.BeginUnitOfWork();
        return scope.GetRegisteredObject<IRepository<ISyncState>>().GetById(id);
    }
    private static ISnapshot Snapshot(EventSourcedDomainContext context, Guid id)
    {
        using var scope = context.BeginUnitOfWork();
        return scope.GetRegisteredObject<ISnapshotRepository>().GetById(id);
    }
    private int Receipts()
    {
        using var connection = new SQLiteConnection(remotePath);
        return connection.ExecuteScalar<int>("select count(*) from SyncReceipt");
    }

    private sealed class AllowRebase : ISyncRebasePolicy
    {
        public int Calls;
        public bool CanRebase(IList<IAggregateEvent> local, IList<IAggregateEvent> remote) { Calls++; return true; }
    }
    private sealed class HookRemote : ISyncRemote
    {
        private readonly ISyncRemote inner;
        private readonly Func<SyncBatch, Task> append;
        public string RemoteId => inner.RemoteId;
        public HookRemote(ISyncRemote inner, Func<SyncBatch, Task> append) { this.inner = inner; this.append = append; }
        public Task<SyncStream> ReadAsync(Guid id, CancellationToken token = default) => inner.ReadAsync(id, token);
        public Task AppendAsync(SyncBatch batch, CancellationToken token = default) => append(batch);
    }

    private EventSourcedDomainContext Context(string path)
    {
        var context = new EventSourcedDomainContext(path, modelsPath, TestEventSerializer.Instance) {
            CommandSerializer = new CommandSerializer(), SnapshotSerializer = new SnapshotSerializer(),
        };
        context.Register(AggregateRegistration.ForType<TestAggregateRoot>());
        return context;
    }

    private static void Execute(EventSourcedDomainContext context, Guid id, params int[] values)
        => context.Execute<TestAggregateRoot>(values.Select(value => (IAggregateCommand)new TestCommand1 {
            AggregateId = id, Value2 = value,
        }).ToList());

    private static TestEvent1 Event(Guid id, int version, int value) => new() {
        AggregateId = id, AggregateType = AggregateRootBase.GetAggregateTypeDescriptor<TestAggregateRoot>(),
        Version = version, Value2 = value, CommandId = Guid.NewGuid(),
    };

    private sealed class CommandSerializer : ISerializer<IAggregateCommand>
    {
        public string SerializeToString(object value) => JsonSerializer.Serialize((TestCommand1)value);
        public IAggregateCommand DeserializeFromString(string data) => JsonSerializer.Deserialize<TestCommand1>(data);
    }
    private sealed class SnapshotSerializer : ISerializer<ISnapshot>
    {
        public string SerializeToString(object value) => JsonSerializer.Serialize((TestSnapshot)value);
        public ISnapshot DeserializeFromString(string data) => JsonSerializer.Deserialize<TestSnapshot>(data);
    }
}
