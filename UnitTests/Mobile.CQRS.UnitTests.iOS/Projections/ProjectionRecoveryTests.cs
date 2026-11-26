using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mobile.CQRS.Reactive;
using Mobile.CQRS;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Domain.UnitTests;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.SQLite.Domain;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Projections;

[TestFixture]
public class ProjectionRecoveryTests
{
    private string eventsPath, modelsPath;

    [SetUp]
    public void SetUp()
    {
        eventsPath = Path.Combine(Path.GetTempPath(), $"events-{Guid.NewGuid()}.db");
        modelsPath = Path.Combine(Path.GetTempPath(), $"models-{Guid.NewGuid()}.db");
    }

    [TearDown]
    public void TearDown() { File.Delete(eventsPath); File.Delete(modelsPath); File.Delete(modelsPath + ".crash-point"); }

    [TestCase(false)]
    [TestCase(true)]
    public void OutboxCommitsOrRollsBackWithEvents(bool commit)
    {
        var context = new EventSourcedDomainContext(eventsPath, modelsPath, TestEventSerializer.Instance);
        var registration = AggregateRegistration.ForType<TestAggregateRoot>();
        context.Register(registration);
        using (var setup = context.BeginUnitOfWork()) setup.Commit();
        using (var scope = context.BeginUnitOfWork())
        {
            new DomainCommandExecutor(scope, registration, new ObservableDomainNotificationBus()).Execute(
                new IAggregateCommand[] { new TestCommand1 { AggregateId = Guid.NewGuid(), Value2 = 3 } }, 0);
            Assert.AreEqual(1, scope.GetRegisteredObject<SQLiteConnection>()
                .ExecuteScalar<int>("select count(*) from ReadModelWorkItem"));
            using var reader = new SQLiteConnection(eventsPath);
            Assert.AreEqual(0, reader.ExecuteScalar<int>("select count(*) from ReadModelWorkItem"));
            if (commit) scope.Commit();
        }
        using var reopened = new SQLiteConnection(eventsPath);
        Assert.AreEqual(commit ? 1 : 0, reopened.ExecuteScalar<int>("select count(*) from AggregateEvent"));
        Assert.AreEqual(commit ? 1 : 0, reopened.ExecuteScalar<int>("select count(*) from ReadModelWorkItem"));
    }

    [Test]
    public async Task ProjectionFailureRollsBackOutputAndCheckpointAndCanRetry()
    {
        var id = Guid.NewGuid();
        var context = Context(evt => { if (evt.Version == 2) throw new InvalidOperationException("injected projection failure"); });
        Append(context, id, 2, 3);
        var failed = await Run(context, expectFault: true);
        Assert.IsTrue(failed.IsFaulted);
        Assert.AreEqual(0, ModelCount());
        Assert.IsNull(Checkpoint(id));
        Assert.AreEqual(1, OutboxCount());

        await Run(Context()); // restart without another command or notification
        Assert.AreEqual(5, Row(id).Total);
        Assert.AreEqual(2, Row(id).Applications);
        Assert.AreEqual(2, Checkpoint(id).Version);
        Assert.AreEqual(0, OutboxCount());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RedeliveryAfterProjectionCommitSkipsApplication(bool rebuild)
    {
        var id = Guid.NewGuid();
        var applications = 0;
        var context = Context(_ => Interlocked.Increment(ref applications));
        Append(context, id, 2, 3);
        if (rebuild) context.RebuildReadModels<TestAggregateRoot>(id);
        await Run(context, expectFault: true, beforeAck: _ => throw new InvalidOperationException("lost acknowledgement"));
        Assert.AreEqual(2, applications);
        Assert.AreEqual(5, Row(id).Total);
        Assert.AreEqual(2, Checkpoint(id).Version);
        Assert.AreEqual(1, OutboxCount());

        await Run(Context(_ => Interlocked.Increment(ref applications)));
        Assert.AreEqual(2, applications);
        Assert.AreEqual(2, Row(id).Applications);
        Assert.AreEqual(0, OutboxCount());
    }

    [Test]
    public void OldAcknowledgementCannotDeleteANewerIntentAtTheSameVersion()
    {
        var context = Context();
        var id = Guid.NewGuid();
        IReadModelWorkItem old, latest;
        using (var scope = context.BeginUnitOfWork())
        {
            var producer = scope.GetRegisteredObject<IReadModelQueueProducer>();
            old = producer.Enqueue(id, "type", 1);
            scope.Commit();
        }
        using (var scope = context.BeginUnitOfWork())
        {
            latest = scope.GetRegisteredObject<IReadModelQueueProducer>().Enqueue(id, "type", 1);
            new ReadModelBuilderQueue(scope).Dequeue(old);
            scope.Commit();
        }
        Assert.AreNotEqual(old.DeliveryId, latest.DeliveryId);
        Assert.AreEqual(1, OutboxCount());
        using (var scope = context.BeginUnitOfWork())
        {
            new ReadModelBuilderQueue(scope).Dequeue(latest);
            scope.Commit();
        }
        Assert.AreEqual(0, OutboxCount());
    }

    [Test]
    public async Task AppendDuringProjectionKeepsNewWorkAndAppliesOnlyTheNewTail()
    {
        var id = Guid.NewGuid();
        EventSourcedDomainContext context = null;
        context = Context(evt => { if (evt.Version == 1) Append(context, id, 3); });
        Append(context, id, 2);
        await Run(context, acknowledgements: 2);
        Assert.AreEqual(5, Row(id).Total);
        Assert.AreEqual(2, Row(id).Applications);
        Assert.AreEqual(0, OutboxCount());
    }

    [Test]
    public async Task ExplicitRebuildReplacesCorruptProjectionAndResetsCheckpoint()
    {
        var context = Context();
        var id = Guid.NewGuid();
        Append(context, id, 2, 3);
        await Run(context);
        using (var connection = new SQLiteConnection(modelsPath))
            connection.Execute("update ProjectionRow set Total = 999, Applications = 99");
        context.RebuildReadModels<TestAggregateRoot>(id);
        await Run(Context());
        Assert.AreEqual(5, Row(id).Total);
        Assert.AreEqual(2, Row(id).Applications);
        Assert.AreEqual(2, Checkpoint(id).Version);
    }

    [Test]
    public async Task FirstRemoteImportProducesProjectionWorkEvenWithoutPreviousSync()
    {
        var context = Context();
        context.CommandSerializer = new CommandSerializer();
        var id = Guid.NewGuid();
        var remote = new InMemoryEventStore();
        remote.SaveEvents(id, new IAggregateEvent[] { new TestEvent1 {
            AggregateId = id, AggregateType = AggregateRootBase.GetAggregateTypeDescriptor<TestAggregateRoot>(),
            Version = 1, Value2 = 7,
        } }, 0);
        context.SyncSomething<TestAggregateRoot>(remote, id);
        Assert.AreEqual(1, OutboxCount());
        await Run(context);
        Assert.AreEqual(7, Row(id).Total);
    }

    [Test]
    public async Task UnknownRegistrationFaultsAndRetainsWork()
    {
        var context = Context();
        using (var scope = context.BeginUnitOfWork())
        {
            scope.GetRegisteredObject<IReadModelQueueProducer>().Enqueue(Guid.NewGuid(), "unknown", 0);
            scope.Commit();
        }
        var agent = await Run(context, expectFault: true);
        StringAssert.Contains("No projection registration", agent.Fault.Message);
        Assert.AreEqual(1, OutboxCount());
    }

    [Test]
    public async Task LostNotificationIsRecoveredByPolling()
    {
        var context = Context();
        using (var setup = context.BeginUnitOfWork()) setup.Commit();
        var empty = Signal();
        var ack = Signal();
        var agent = Agent(context, _ => ack.TrySetResult(true), onEmpty: () => empty.TrySetResult(true));
        agent.Trigger(); // safe before start
        agent.Start();
        try
        {
            await empty.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var id = Guid.NewGuid();
            using (var scope = context.BeginUnitOfWork())
            {
                // Bypass command notifications: the store must create its own durable intent.
                scope.GetRegisteredObject<IEventStore>().SaveEvents(id, new IAggregateEvent[] { new TestEvent1 {
                    AggregateId = id, AggregateType = AggregateRootBase.GetAggregateTypeDescriptor<TestAggregateRoot>(),
                    Version = 1, Value2 = 4,
                } }, 0);
                scope.Commit();
            }
            await ack.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { await agent.StopAsync(); }
        Assert.IsFalse(agent.IsFaulted);
        Assert.AreEqual(0, OutboxCount());
        Assert.AreEqual(1, ModelCount());
    }

    [Test]
    public async Task StopWaitsForWorkerAndRestartRecoversCancelledWork()
    {
        var id = Guid.NewGuid();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blocked = false;
        var context = Context(_ => {
            if (blocked) return;
            blocked = true;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });
        Append(context, id, 2);
        var ack = Signal();
        var agent = Agent(context, _ => ack.TrySetResult(true));
        agent.Start();
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            var stopping = agent.StopAsync();
            Assert.IsFalse(stopping.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => agent.Start());
            release.Set();
            await stopping;
            Assert.AreEqual(1, OutboxCount());
            Assert.AreEqual(0, ModelCount());
            agent.Start();
            await ack.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.Set(); await agent.StopAsync(); }
        Assert.AreEqual(2, Row(id).Total);
        Assert.AreEqual(1, Row(id).Applications);
    }

    [Test]
    public async Task ModelNotificationsObserveCommittedOutputAndAcknowledgedWork()
    {
        var context = Context();
        var id = Guid.NewGuid();
        Append(context, id, 2);
        var observed = false;
        using var subscription = context.EventBus.Subscribe(notification => {
            if (notification.Event is IModelChangeEvent)
            {
                observed = Row(id).Total == 2 && Checkpoint(id).Version == 1 && OutboxCount() == 0;
            }
        });
        await Run(context);
        Assert.IsTrue(observed);
    }


    [Test]
    public async Task TransientProjectionFailureRetriesWithoutAnotherNotification()
    {
        var id = Guid.NewGuid();
        var attempts = 0;
        var context = Context(_ => {
            if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("transient");
        });
        Append(context, id, 2);
        await Run(context);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, Row(id).Applications);
        Assert.AreEqual(0, OutboxCount());
    }

    [Test]
    public async Task ContextStartsDurableWorkAndAsyncDisposalJoinsWorker()
    {
        var context = Context();
        var id = Guid.NewGuid();
        Append(context, id, 2);
        var notification = Signal();
        using var subscription = context.EventBus.Subscribe(evt => {
            if (evt.Event is IModelChangeEvent) notification.TrySetResult(true);
        });
        context.StartDelayedReadModels();
        context.StartDelayedReadModels();
        try { await notification.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await context.DisposeAsync(); }
        Assert.IsFalse(context.BuilderAgent.IsStarted);
        Assert.AreEqual(0, OutboxCount());
        Assert.AreEqual(1, Row(id).Applications);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ProcessExitRecoversWithoutDoubleApplication(bool afterCommit)
    {
        var id = Guid.NewGuid();
        var context = Context();
        Append(context, id, 2, 3);
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(ProjectionRecoveryTests).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName=Mobile.CQRS.UnitTests.Projections.ProjectionRecoveryTests.CrashAfterProjectionCommitProbe");
        start.Environment["CQRS_CRASH_EVENTS"] = eventsPath;
        start.Environment["CQRS_CRASH_MODELS"] = modelsPath;
        start.Environment["CQRS_CRASH_AFTER_COMMIT"] = afterCommit.ToString();
        using var child = System.Diagnostics.Process.Start(start);
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        Assert.AreNotEqual(0, child.ExitCode, await output + await error);
        if (afterCommit)
        {
            // Prove the child got past target COMMIT, not merely that its host failed.
            Assert.AreEqual(5, Row(id).Total);
            Assert.AreEqual(2, Checkpoint(id).Version);
        }
        else
        {
            Assert.AreEqual(0, ModelCount());
            Assert.IsNull(Checkpoint(id));
            Assert.IsTrue(File.Exists(modelsPath + ".crash-point"), "Child must reach its uncommitted projection write.");
        }
        Assert.AreEqual(1, OutboxCount());
        await Run(Context());
        Assert.AreEqual(5, Row(id).Total);
        Assert.AreEqual(2, Row(id).Applications);
        Assert.AreEqual(0, OutboxCount());
    }

    [Test, Explicit("Run only in the subprocess crash-recovery test")]
    public void CrashAfterProjectionCommitProbe()
    {
        eventsPath = Environment.GetEnvironmentVariable("CQRS_CRASH_EVENTS");
        modelsPath = Environment.GetEnvironmentVariable("CQRS_CRASH_MODELS");
        Assert.IsNotNull(eventsPath);
        Assert.IsNotNull(modelsPath);
        var afterCommit = bool.Parse(Environment.GetEnvironmentVariable("CQRS_CRASH_AFTER_COMMIT"));
        var context = Context(_ => {
            if (afterCommit) return;
            File.WriteAllText(modelsPath + ".crash-point", "projection-write-reached");
            Environment.Exit(73);
        });
        var agent = Agent(context, _ => Environment.Exit(73));
        agent.Start();
        using var never = new ManualResetEventSlim();
        never.Wait(TimeSpan.FromSeconds(10));
        Assert.Fail("The worker did not reach acknowledgement.");
    }


    [Test]
    public async Task ExistingProjectionWithoutCheckpointIsRebuiltFromHistory()
    {
        var id = Guid.NewGuid();
        var context = Context();
        using (var connection = new SQLiteConnection(modelsPath))
            connection.Insert(new ProjectionRow { Identity = id, Total = 999, Applications = 99 });
        Append(context, id, 2, 3);
        await Run(context);
        Assert.AreEqual(5, Row(id).Total);
        Assert.AreEqual(2, Row(id).Applications);
    }

    [Test]
    public async Task FailureInSecondBuilderRollsBackTheWholeProjectionGroup()
    {
        var context = new EventSourcedDomainContext(eventsPath, modelsPath, TestEventSerializer.Instance);
        using (var setup = new SQLiteConnection(modelsPath))
        {
            setup.CreateTable<ProjectionRow>();
            setup.CreateTable<ReadModelCheckpointRecord>();
        }
        context.Register(AggregateRegistration.ForType<TestAggregateRoot>()
            .WithDelayedReadModel(scope => new CountingBuilder(scope.GetRegisteredObject<SQLiteConnection>(), null))
            .WithDelayedReadModel(_ => new FailingBuilder()));
        var id = Guid.NewGuid();
        Append(context, id, 2);
        await Run(context, expectFault: true);
        Assert.AreEqual(0, ModelCount());
        Assert.IsNull(Checkpoint(id));
        Assert.AreEqual(1, OutboxCount());
    }

    [Test]
    public async Task PermanentFailurePausesAfterThreeAttemptsUntilTriggered()
    {
        var context = Context(_ => throw new InvalidOperationException("permanent"));
        Append(context, Guid.NewGuid(), 2);
        var third = Signal();
        var fourth = Signal();
        var failures = 0;
        using var subscription = context.EventBus.Subscribe(evt => {
            if (evt.Event is not IReadModelBuilderFaulted) return;
            var count = Interlocked.Increment(ref failures);
            if (count == 3) third.TrySetResult(true);
            if (count == 4) fourth.TrySetResult(true);
        });
        var agent = Agent(context, _ => Assert.Fail("Failed work must not be acknowledged."));
        agent.Start();
        try
        {
            await third.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreNotEqual(fourth.Task, await Task.WhenAny(fourth.Task, Task.Delay(1200)));
            agent.Trigger();
            await fourth.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { await agent.StopAsync(); }
        Assert.AreEqual(4, failures);
        Assert.AreEqual(1, OutboxCount());
    }


    [Test]
    public void FailedCommandCommitRollsBackItsOutboxIntent()
    {
        var context = new EventSourcedDomainContext(eventsPath, modelsPath, TestEventSerializer.Instance);
        context.Register(AggregateRegistration.ForType<TestAggregateRoot>().WithImmediateReadModel(_ => new FailingBuilder()));
        using (var setup = context.BeginUnitOfWork()) setup.Commit();
        Assert.Throws<InvalidOperationException>(() => Append(context, Guid.NewGuid(), 2));
        using var reader = new SQLiteConnection(eventsPath);
        Assert.AreEqual(0, reader.ExecuteScalar<int>("select count(*) from AggregateEvent"));
        Assert.AreEqual(0, OutboxCount());
    }

    private EventSourcedDomainContext Context(Action<TestEvent1> onApply = null)
    {
        using (var setup = new SQLiteConnection(modelsPath))
        {
            setup.CreateTable<ProjectionRow>();
            setup.CreateTable<ReadModelCheckpointRecord>();
        }
        var context = new EventSourcedDomainContext(eventsPath, modelsPath, TestEventSerializer.Instance);
        context.Register(AggregateRegistration.ForType<TestAggregateRoot>().WithDelayedReadModel(
            scope => new CountingBuilder(scope.GetRegisteredObject<SQLiteConnection>(), onApply)));
        return context;
    }

    private static void Append(EventSourcedDomainContext context, Guid id, params int[] values)
    {
        context.Execute<TestAggregateRoot>(values.Select(value => (IAggregateCommand)new TestCommand1 {
            AggregateId = id, Value2 = value,
        }).ToList());
    }

    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task<ReadModelBuilderAgent> Run(EventSourcedDomainContext context, bool expectFault = false,
        Action<IReadModelWorkItem> beforeAck = null, int acknowledgements = 1)
    {
        var done = Signal();
        var count = 0;
        using var faults = context.EventBus.Subscribe(notification => {
            if (expectFault && notification.Event is IReadModelBuilderFaulted) done.TrySetResult(true);
        });
        var agent = Agent(context, item => {
            beforeAck?.Invoke(item);
            if (!expectFault && Interlocked.Increment(ref count) == acknowledgements) done.TrySetResult(true);
        });
        agent.Start();
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await agent.StopAsync(); }
        Assert.AreEqual(expectFault, agent.IsFaulted, agent.Fault?.ToString());
        return agent;
    }

    private ReadModelBuilderAgent Agent(EventSourcedDomainContext context, Action<IReadModelWorkItem> onAck, Action onEmpty = null)
        => new(context, context.Registrations.ToList(), context.BeginReadModelUnitOfWork,
            scope => new ObservedQueue(new ReadModelBuilderQueue(scope), onAck, onEmpty));

    private int OutboxCount()
    {
        using var connection = new SQLiteConnection(eventsPath);
        return connection.ExecuteScalar<int>("select count(*) from ReadModelWorkItem");
    }
    private int ModelCount()
    {
        using var connection = new SQLiteConnection(modelsPath);
        return connection.ExecuteScalar<int>("select count(*) from ProjectionRow");
    }
    private ProjectionRow Row(Guid id)
    {
        using var connection = new SQLiteConnection(modelsPath);
        return connection.Find<ProjectionRow>(id);
    }
    private ReadModelCheckpoint Checkpoint(Guid id)
    {
        using var connection = new SQLiteConnection(modelsPath);
        return new ReadModelCheckpointStore(connection).Get(id);
    }

    public sealed class ProjectionRow : IUniqueId
    {
        [PrimaryKey]
        public Guid Identity { get; set; }
        public int Total { get; set; }
        public int Applications { get; set; }
    }

    private sealed class CountingBuilder : ReadModelBuilder<ProjectionRow>
    {
        private readonly Action<TestEvent1> onApply;
        public CountingBuilder(SQLiteConnection connection, Action<TestEvent1> onApply)
            : base(new SqlRepository<ProjectionRow>(connection, "Identity")) { this.onApply = onApply; }
        public void Apply(TestEvent1 evt)
        {
            var row = Repository.GetById(evt.AggregateId) ?? new ProjectionRow { Identity = evt.AggregateId };
            row.Total += evt.Value2;
            row.Applications++;
            Repository.Save(row);
            onApply?.Invoke(evt);
        }
    }

    private sealed class ObservedQueue : IReadModelQueue
    {
        private readonly IReadModelQueue inner;
        private readonly Action<IReadModelWorkItem> onAck;
        private readonly Action onEmpty;
        public ObservedQueue(IReadModelQueue inner, Action<IReadModelWorkItem> onAck, Action onEmpty)
            { this.inner = inner; this.onAck = onAck; this.onEmpty = onEmpty; }
        public void Dequeue(IReadModelWorkItem item) { onAck(item); inner.Dequeue(item); }
        public IList<IReadModelWorkItem> Peek(int size)
        {
            var items = inner.Peek(size);
            if (items.Count == 0) onEmpty?.Invoke();
            return items;
        }
    }

    private sealed class FailingBuilder : IReadModelBuilder
    {
        public IEnumerable<IDomainNotification> Handle(IDomainNotification evt) => throw new InvalidOperationException("second builder failed");
        public IEnumerable<IDomainNotification> Process(IEnumerable<IAggregateEvent> events) => throw new InvalidOperationException("second builder failed");
        public IEnumerable<IDomainNotification> Process(IEnumerable<IDomainNotification> events) => throw new InvalidOperationException("second builder failed");
        public void DeleteForAggregate(Guid id) { }
    }

    private sealed class CommandSerializer : Mobile.CQRS.Serialization.ISerializer<IAggregateCommand>
    {
        public string SerializeToString(object value) => System.Text.Json.JsonSerializer.Serialize((TestCommand1)value);
        public IAggregateCommand DeserializeFromString(string value) => System.Text.Json.JsonSerializer.Deserialize<TestCommand1>(value);
    }

}
