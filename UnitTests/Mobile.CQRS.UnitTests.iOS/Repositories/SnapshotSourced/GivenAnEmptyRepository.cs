
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories.SnapshotSourced
{
    [TestFixture]
    public class GivenAnEmptyRepository : GivenASnapshotSourcedAggregateRepository
    {
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
        }
        
        [Test]
        public void WhenRetreivingAnAggregate_ThenNullIsReturned()
        {
            var id = Guid.NewGuid();
            var result = this.Repository.GetByIdAsync(id).Result;

            Assert.AreEqual(null, result);
        }
    }
}
