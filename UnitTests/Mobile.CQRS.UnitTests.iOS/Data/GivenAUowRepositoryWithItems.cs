
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Core.UnitTests.Data
{
    [TestFixture]
    public class GivenAUowRepositoryWithItems : GivenAUnitOfWorkRepository
    {
        public IdTest Item1 { get; private set; }
        public IdTest Item2 { get; private set; }
        public IdTest Item3 { get; private set; }
        
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            this.Item1 = this.UnitOfWork.New();
            this.Item2 = this.UnitOfWork.New();
            this.Item3 = this.UnitOfWork.New();
            
            this.Item1.Value = 1;
            this.Item2.Value = 2;
            this.Item3.Value = 3;

            // add to underlying repository - initial data set
            this.Repository.SaveAsync(this.Item1).Wait();
            this.Repository.SaveAsync(this.Item2).Wait();
            this.Repository.SaveAsync(this.Item3).Wait();
        }
        
        [Test]
        public void WhenUpdatingAnItem_ThenTheItemSaveResultIsUpdated()
        {
            var item = new IdTest() { Identity = this.Item1.Identity, Value = 123, };
            var result = this.UnitOfWork.SaveAsync(item).Result;

            Assert.AreEqual(SaveResult.Updated, result);
        }
        
        [Test]
        public void WhenUpdatingAnItem_ThenTheItemIsUpdated()
        {
            var item = new IdTest() { Identity = this.Item1.Identity, Value = 123, };
            this.UnitOfWork.SaveAsync(item).Wait();
            var updated = this.UnitOfWork.GetByIdAsync(item.Identity).Result;
            Assert.AreEqual(123, updated.Value);
        }
        
        [Test]
        public void WhenUpdatingAnItem_ThenTheItemIsNotUpdatedInTheUnderlyingRepository()
        {
            var item = new IdTest() { Identity = this.Item1.Identity, Value = 123, };
            this.UnitOfWork.SaveAsync(item).Wait();

            var updated = this.Repository.GetByIdAsync(item.Identity).Result;
            Assert.AreNotEqual(123, updated.Value);
        }
        
        [Test]
        public void WhenUpdatingAnItemAndCommitting_ThenTheItemIsUpdatedInTheUnderlyingRepository()
        {
            var item = new IdTest() { Identity = this.Item1.Identity, Value = 123, };
            this.UnitOfWork.SaveAsync(item).Wait();
            this.UnitOfWork.CommitAsync().Wait();
            
            var updated = this.Repository.GetByIdAsync(item.Identity).Result;
            Assert.AreEqual(123, updated.Value);
        }

        [Test]
        public void WhenDeletingAnItem_ThenTheItemIsRemoved()
        {
            this.UnitOfWork.DeleteAsync(this.Item2).Wait();
            var count = this.UnitOfWork.GetAllAsync().Result.Count;
            Assert.AreEqual(2, count);
        }

        [Test]
        public void WhenDeletingAnItem_ThenTheItemIsNotRemovedFromTheUnderlyingRepository()
        {
            this.UnitOfWork.DeleteAsync(this.Item2).Wait();

            var count = this.Repository.GetAllAsync().Result.Count;
            Assert.AreEqual(3, count);
        }

        [Test]
        public void WhenDeletingAnItemAndCommitting_ThenTheItemIsRemovedFromTheUnderlyingRepository()
        {
            this.UnitOfWork.DeleteAsync(this.Item2).Wait();
            this.UnitOfWork.CommitAsync().Wait();
            
            var count = this.Repository.GetAllAsync().Result.Count;
            Assert.AreEqual(2, count);
        }

        [Test]
        public void WhenDeletingAnItemById_ThenTheItemIsRemoved()
        {
            this.UnitOfWork.DeleteIdAsync(this.Item2.Identity).Wait();
            var count = this.UnitOfWork.GetAllAsync().Result.Count;
            Assert.AreEqual(2, count);
        }
        
        [Test]
        public void WhenDeletingAnItem_ThenTheItemIsNoLongerInTheRepository()
        {
            this.UnitOfWork.DeleteAsync(this.Item2).Wait();
            var deleted = this.UnitOfWork.GetByIdAsync(this.Item2.Identity).Result;
            Assert.AreEqual(null, deleted);
        }
        
        [Test]
        public void WhenDeletingAllItems_ThenThereAreNoItems()
        {
            this.UnitOfWork.DeleteIdAsync(this.Item1.Identity).Wait();
            this.UnitOfWork.DeleteIdAsync(this.Item2.Identity).Wait();
            this.UnitOfWork.DeleteIdAsync(this.Item3.Identity).Wait();
            var count = this.UnitOfWork.GetAllAsync().Result.Count;
            Assert.AreEqual(0, count);
        }
    }
}
