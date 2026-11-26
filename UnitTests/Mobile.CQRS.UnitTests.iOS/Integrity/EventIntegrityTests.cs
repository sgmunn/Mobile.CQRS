using System;
using System.Linq;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Domain.UnitTests;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Integrity;

[TestFixture]
public class EventIntegrityTests
{
    [Test]
    public void CallerCannotMutateStoredEventsThroughInputOrReadResults()
    {
        var store = new InMemoryEventStore();
        var id = Guid.NewGuid();
        var evt = Event(id, 1);
        store.SaveEvents(id, new[] { evt }, 0);
        evt.Version = 90;
        evt.Value2 = 999;
        var read = (TestEvent1)store.GetAllEvents(id).Single();
        Assert.AreEqual(1, read.Version);
        Assert.AreEqual(7, read.Value2);
        read.Version = 40;
        read.Value2 = 123;
        Assert.AreEqual(1, store.GetCurrentVersion(id));
        Assert.AreEqual(7, ((TestEvent1)store.GetAllEvents(id).Single()).Value2);
    }

    [Test]
    public void ReplayRejectsAnInvalidBatchBeforeApplyingAnyEvent()
    {
        var id = Guid.NewGuid();
        var aggregate = new TestAggregateRoot { Identity = id };
        Assert.Throws<InvalidOperationException>(() => aggregate.LoadFromEvents(new IAggregateEvent[] {
            Event(id, 1), Event(Guid.NewGuid(), 3),
        }));
        Assert.AreEqual(0, aggregate.Version);
        Assert.AreEqual(0, aggregate.Value2);
    }


    [TestCase("gap")]
    [TestCase("wrong-id")]
    [TestCase("wrong-type")]
    [TestCase("duplicate-id")]
    public void ReplayValidatesEveryEnvelopeBeforeApplyingTheBatch(string kind)
    {
        var id = Guid.NewGuid();
        var first = Event(id, 1);
        var second = Event(id, 2);
        if (kind == "gap") second.Version = 3;
        if (kind == "wrong-id") second.AggregateId = Guid.NewGuid();
        if (kind == "wrong-type") second.AggregateType = "other";
        if (kind == "duplicate-id") second.Identity = first.Identity;
        var root = new TestAggregateRoot { Identity = id };
        Assert.Throws<InvalidOperationException>(() => root.LoadFromEvents(new IAggregateEvent[] { first, second }));
        Assert.AreEqual(0, root.Version);
        Assert.AreEqual(0, root.Value2);
    }

    [Test]
    public void ReplayRejectsReusedEventIdentityAcrossCalls()
    {
        var id = Guid.NewGuid();
        var first = Event(id, 1);
        var root = new TestAggregateRoot { Identity = id };
        root.LoadFromEvents(new IAggregateEvent[] { first });
        var second = Event(id, 2);
        second.Identity = first.Identity;
        Assert.Throws<InvalidOperationException>(() => root.LoadFromEvents(new IAggregateEvent[] { second }));
        Assert.AreEqual(1, root.Version);
    }

    [Test]
    public void FailedApplyDoesNotAdvanceVersionOrEnqueueAnEvent()
    {
        var root = new FailingAggregate { Identity = Guid.NewGuid() };
        Assert.Throws<System.Reflection.TargetInvocationException>(() => root.Emit());
        Assert.AreEqual(0, root.Version);
        Assert.IsEmpty(root.UncommittedEvents);
        root.Fail = false;
        root.Emit();
        Assert.AreEqual(1, root.Version);
        Assert.AreEqual(1, root.UncommittedEvents.Single().Version);
    }

    private sealed class FailingAggregate : AggregateRootBase
    {
        public bool Fail = true;
        public void Emit() => RaiseEvent(Guid.NewGuid(), new TestEvent1());
        private void Apply(TestEvent1 evt) { if (Fail) throw new InvalidOperationException(); }
    }


    [Test]
    public void InMemorySnapshotsAreDetachedThroughBothRepositoryInterfaces()
    {
        var snapshots = new InMemorySnapshotRepository<TestSnapshot>();
        var id = Guid.NewGuid();
        var original = new TestSnapshot { Identity = id, Version = 1, Value2 = 7 };
        snapshots.Save(original, 0);
        original.Version = 90;
        var read = (TestSnapshot)((ISnapshotRepository)snapshots).GetById(id);
        Assert.AreEqual(1, read.Version);
        read.Value2 = 99;
        var generic = (IRepository<TestSnapshot>)snapshots;
        generic.GetById(id).Version = 80;
        generic.GetAll().Single().Version = 70;
        Assert.AreEqual(7, generic.GetById(id).Value2);
        Assert.AreEqual(1, generic.GetById(id).Version);
        Assert.Throws<ConcurrencyException>(() => snapshots.Save(new TestSnapshot { Identity = Guid.NewGuid(), Version = 2 }, 1));
    }

    [Test]
    public void SnapshotAheadOfHistoryFallsBackToCompleteReplay()
    {
        var id = Guid.NewGuid();
        var events = new InMemoryEventStore();
        events.SaveEvents(id, new[] { Event(id, 1) }, 0);
        var snapshots = new InMemorySnapshotRepository<TestSnapshot>();
        snapshots.Save(new TestSnapshot { Identity = id, Version = 100, Value2 = 999 });
        var repository = new AggregateRepository(() => new TestAggregateRoot(), events, snapshots);
        var restored = (TestAggregateRoot)repository.GetById(id);
        Assert.AreEqual(7, restored.Value2);
        Assert.AreEqual(1, restored.Version);
    }

    internal static TestEvent1 Event(Guid id, int version) => new() {
        AggregateId = id, Version = version, Value2 = 7,
        AggregateType = AggregateRootBase.GetAggregateTypeDescriptor<TestAggregateRoot>(),
    };
}
