
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories.EventSourcing
{
    [TestFixture]
    public class GivenAnAggregateWith1000StoredEvents : GivenAnEventSourcedAggregateRepository
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

            this.Repository.SaveAsync(this.Aggregate).Wait();
        }
        
        [Test]
        public void WhenLoadingTheAggregate_ThenThePerformanceIsOK()
        {
            // we actually have to test this manually by seeing how long it takes for real
            var result = this.Repository.GetByIdAsync(this.Aggregate.Identity).Result;
            Assert.AreNotEqual(null, result);
            Assert.AreNotEqual(0, result.Version);
        }
    }
}
