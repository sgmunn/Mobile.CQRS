
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories.SnapshotSourced
{
    [TestFixture]
    public class GivenAnAggregateWithNoUncommittedEvents : GivenASnapshotSourcedAggregateRepository
    {
        public TestAggregateRoot Aggregate { get; private set; }
        
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            
            this.Aggregate = new TestAggregateRoot();
        }
        
        [Test]
        public void WhenSavingTheAggregate_ThenTheSaveResultIsNone()
        {
            var result = this.Repository.SaveAsync(this.Aggregate).Result;
            Assert.AreEqual(SaveResult.None, result);
        }
    }
}
