
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Core.UnitTests.Data
{
    [TestFixture]
    public class GivenAnEmptyUowRepository : GivenAUnitOfWorkRepository
    {
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
        }
        
        [Test]
        public void WhenGettingAllItems_ThenNoItemsAreReturned()
        {
            var items = this.UnitOfWork.GetAllAsync().Result;
            Assert.AreEqual(0, items.Count);
        }
        
        [Test]
        public void WhenCreatingANewItem_ThenAnItemIsReturned()
        {
            var item = this.UnitOfWork.New();
            Assert.AreNotEqual(null, item);
        }
        
        [Test]
        public void WhenAddingAnItem_ThenOneItemIsAdded()
        {
            this.UnitOfWork.SaveAsync(this.UnitOfWork.New()).Wait();
            var count = this.UnitOfWork.GetAllAsync().Result.Count;
            Assert.AreEqual(1, count);
        }
        
        [Test]
        public void WhenAddingAnItem_ThenTheUnderlyingRepositoryIsNotUpdated()
        {
            this.UnitOfWork.SaveAsync(this.UnitOfWork.New()).Wait();
            var count = this.Repository.GetAllAsync().Result.Count;
            Assert.AreEqual(0, count);
        }
        
        [Test]
        public void WhenAddingAnItemAndCommitting_ThenTheUnderlyingRepositoryIsUpdated()
        {
            this.UnitOfWork.SaveAsync(this.UnitOfWork.New()).Wait();
            this.UnitOfWork.CommitAsync().Wait();

            var count = this.Repository.GetAllAsync().Result.Count;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void WhenAddingAnItem_ThenTheSaveResultIsAdded()
        {
            var saveResult = this.UnitOfWork.SaveAsync(this.UnitOfWork.New()).Result;
            Assert.AreEqual(SaveResult.Added, saveResult);
        }

        [Test]
        public void WhenAddingAnItem_ThenTheItemIsReturned()
        {
            var item = this.UnitOfWork.New();
            this.UnitOfWork.SaveAsync(item).Wait();
            var result = this.UnitOfWork.GetByIdAsync(item.Identity).Result;
            Assert.AreEqual(item, result);
        }

        [Test]
        public void WhenAddingAnItem_ThenTheUnderlyingRepositoryDoesNotContainTheItem()
        {
            var item = this.UnitOfWork.New();
            this.UnitOfWork.SaveAsync(item).Wait();
            var result = this.Repository.GetByIdAsync(item.Identity).Result;
            Assert.AreEqual(null, result);
        }

        [Test]
        public void WhenAddingAnItemAndCommitting_ThenTheUnderlyingRepositoryDoesContainTheItem()
        {
            var item = this.UnitOfWork.New();
            this.UnitOfWork.SaveAsync(item).Wait();
            this.UnitOfWork.CommitAsync().Wait();

            var result = this.Repository.GetByIdAsync(item.Identity).Result;
            Assert.AreEqual(item, result);
        }

        [Test]
        public void WhenAdding10Items_Then10AreReturned()
        {
            for (int i = 0;i<10;i++)
            {
                this.UnitOfWork.SaveAsync(this.UnitOfWork.New()).Wait();
            }
            
            var count = this.UnitOfWork.GetAllAsync().Result.Count;
            Assert.AreEqual(10, count);
        }
    }
}
