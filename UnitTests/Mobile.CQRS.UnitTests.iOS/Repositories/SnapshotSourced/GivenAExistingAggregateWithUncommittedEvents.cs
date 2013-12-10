
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories.SnapshotSourced
{
    [TestFixture]
    public class GivenAExistingAggregateWithUncommittedEvents : GivenASnapshotSourcedAggregateRepository
    {
        public TestAggregateRoot Aggregate { get; private set; }
        
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            
            var id = Guid.NewGuid();
            this.Aggregate = new TestAggregateRoot();
            this.Aggregate.Execute(new TestCommand1{ AggregateId = id, Value1 = "1", Value2 = 1, });
            this.Repository.SaveAsync(this.Aggregate).Wait();

            this.Aggregate.Execute(new TestCommand1{ AggregateId = id, Value1 = "2", Value2 = 2, });
        }
        
        [Test]
        public void WhenSavingTheAggregate_ThenTheSaveResultIsAdded()
        {
            var result = this.Repository.SaveAsync(this.Aggregate).Result;
            Assert.AreEqual(SaveResult.Updated, result);
        }
    }
}
