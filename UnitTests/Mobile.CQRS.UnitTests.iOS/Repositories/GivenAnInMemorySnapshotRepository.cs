
using System;
using NUnit.Framework;
using Mobile.CQRS.InMemory;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class GivenAnInMemorySnapshotRepository
    {
        public ISnapshotRepository SnapshotStore { get; private set; }
        
        public virtual void SetUp()
        {
            this.SnapshotStore = new InMemorySnapshotRepository<TestSnapshot>();
        }
    }
}
