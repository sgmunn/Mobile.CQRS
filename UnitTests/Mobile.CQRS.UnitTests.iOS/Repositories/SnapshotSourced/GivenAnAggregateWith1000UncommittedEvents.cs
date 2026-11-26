
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories.SnapshotSourced
{
    [TestFixture]
    public class GivenAnAggregateWith1000UncommittedEvents : GivenASnapshotSourcedAggregateRepository
    {
        public TestAggregateRoot Aggregate { get; private set; }
        
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            
            var id = Guid.NewGuid();
            this.Aggregate = new TestAggregateRoot();
            
            for (int i = 0; i < 1000; i++)
            {
                this.Aggregate.Execute(new TestCommand1{ AggregateId = id, Value1 = "1", Value2 = i, });
            }
        }
        
        [Test]
        public void WhenSavingTheAggregate_ThenAllEventsAreApplied()
        {
            this.Repository.Save(this.Aggregate);
            var restored = (TestAggregateRoot)this.Repository.GetById(this.Aggregate.Identity);
            Assert.AreEqual(1000, restored.Version);
            Assert.AreEqual(999, restored.Value2);
        }
    }
}
