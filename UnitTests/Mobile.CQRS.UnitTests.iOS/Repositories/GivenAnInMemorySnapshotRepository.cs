
using System;
using NUnit.Framework;

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
