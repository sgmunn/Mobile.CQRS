
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class GivenAnInMemoryEventStoreRepository
    {
        public IEventStoreRepository EventStore { get; private set; }

        public virtual void SetUp()
        {
            this.EventStore = new InMemoryEventStoreRepository<TestSerializedEvent>();
        }
    }
}
