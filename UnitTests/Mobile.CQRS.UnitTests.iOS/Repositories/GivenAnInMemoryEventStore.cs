
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    [TestFixture]
    public class GivenAnInMemoryEventStore : GivenAnInMemoryEventStoreRepository
    {
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
        }

        [Test]
        public void WhenAddingEventsForAnAggregate_ThenAllEventsAreReturned()
        {
            var id = Guid.NewGuid();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 1, }}, 0).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 2, }}, 1).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 3, }}, 2).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 4, }}, 3).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 5, }}, 4).Wait();

            var events = this.EventStore.GetAllEventsAsync(id).Result;
            Assert.AreEqual(5, events.Count);
        }

        [Test]
        public void WhenAddingEventsForAnAggregate_ThenTheEventsAreReturnedInOrder()
        {
            var id = Guid.NewGuid();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 1, }}, 0).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 2, }}, 1).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 3, }}, 2).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 4, }}, 3).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 5, }}, 4).Wait();
            
            var events = this.EventStore.GetAllEventsAsync(id).Result;
            Assert.AreEqual(1, events[0].Version);
            Assert.AreEqual(2, events[1].Version);
            Assert.AreEqual(3, events[2].Version);
            Assert.AreEqual(4, events[3].Version);
            Assert.AreEqual(5, events[4].Version);
        }

        [Test]
        public void WhenAddingEventsForAnAggregate_ThenTheEventsHaveTheCorrectId()
        {
            var id = Guid.NewGuid();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 1, }}, 0).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 2, }}, 1).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 3, }}, 2).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 4, }}, 3).Wait();
            this.EventStore.SaveEventsAsync(id, new[] { new EventBase() {AggregateId = id, Version = 5, }}, 4).Wait();
            
            var events = this.EventStore.GetAllEventsAsync(id).Result;
            Assert.AreEqual(id, events[0].AggregateId);
            Assert.AreEqual(id, events[1].AggregateId);
            Assert.AreEqual(id, events[2].AggregateId);
            Assert.AreEqual(id, events[3].AggregateId);
            Assert.AreEqual(id, events[4].AggregateId);
        }

        [Test]
        public void WhenAddingEventsForMultipleAggregates_ThenTheEventsHaveTheCorrectId()
        {
            var id1 = Guid.NewGuid();
            this.EventStore.SaveEventsAsync(id1, new[] { new EventBase() {AggregateId = id1, Version = 1, }}, 0).Wait();
            this.EventStore.SaveEventsAsync(id1, new[] { new EventBase() {AggregateId = id1, Version = 2, }}, 1).Wait();
            this.EventStore.SaveEventsAsync(id1, new[] { new EventBase() {AggregateId = id1, Version = 3, }}, 2).Wait();
            this.EventStore.SaveEventsAsync(id1, new[] { new EventBase() {AggregateId = id1, Version = 4, }}, 3).Wait();
            this.EventStore.SaveEventsAsync(id1, new[] { new EventBase() {AggregateId = id1, Version = 5, }}, 4).Wait();
            
            var id2 = Guid.NewGuid();
            this.EventStore.SaveEventsAsync(id2, new[] { new EventBase() {AggregateId = id2, Version = 1, }}, 0).Wait();
            this.EventStore.SaveEventsAsync(id2, new[] { new EventBase() {AggregateId = id2, Version = 2, }}, 1).Wait();
            this.EventStore.SaveEventsAsync(id2, new[] { new EventBase() {AggregateId = id2, Version = 3, }}, 2).Wait();
            this.EventStore.SaveEventsAsync(id2, new[] { new EventBase() {AggregateId = id2, Version = 4, }}, 3).Wait();
            this.EventStore.SaveEventsAsync(id2, new[] { new EventBase() {AggregateId = id2, Version = 5, }}, 4).Wait();

            var events1 = this.EventStore.GetAllEventsAsync(id1).Result;
            Assert.AreEqual(id1, events1[0].AggregateId);
            Assert.AreEqual(id1, events1[1].AggregateId);
            Assert.AreEqual(id1, events1[2].AggregateId);
            Assert.AreEqual(id1, events1[3].AggregateId);
            Assert.AreEqual(id1, events1[4].AggregateId);

            var events2 = this.EventStore.GetAllEventsAsync(id2).Result;
            Assert.AreEqual(id2, events2[0].AggregateId);
            Assert.AreEqual(id2, events2[1].AggregateId);
            Assert.AreEqual(id2, events2[2].AggregateId);
            Assert.AreEqual(id2, events2[3].AggregateId);
            Assert.AreEqual(id2, events2[4].AggregateId);
        }
    }
}
