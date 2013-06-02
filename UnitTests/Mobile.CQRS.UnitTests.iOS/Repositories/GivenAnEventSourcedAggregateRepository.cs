
using System;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class GivenAnEventSourcedAggregateRepository : GivenAnInMemoryEventStoreRepository
    {
        public IAggregateRepository Repository { get; private set; }

        public MockBus Bus { get; private set; }

        public override void SetUp()
        {
            base.SetUp();

            this.Bus = new MockBus();

            this.Repository = new AggregateRepository(
                () => new TestAggregateRoot(),
                this.EventStore, 
                null);
            //,
              //  this.Bus);
        }
    }
}
