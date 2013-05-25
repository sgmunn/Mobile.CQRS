
using System;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class GivenAnEventSourcedAggregateRepository : GivenAnInMemoryEventStoreRepository
    {
        public IAggregateRepository<TestAggregateRoot> Repository { get; private set; }

        public MockBus Bus { get; private set; }

        public override void SetUp()
        {
            base.SetUp();

            this.Bus = new MockBus();

            this.Repository = new AggregateRepository<TestAggregateRoot>(
                this.EventStore, 
                null);
            //,
              //  this.Bus);
        }
    }
}
