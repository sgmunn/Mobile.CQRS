
using System;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class MockAggregateManifestRepository : IAggregateManifestRepository
    {
        public void UpdateManifest(string aggregateType, Guid aggregateId, int currentVersion, int newVersion)
        {
        }
    }
}
