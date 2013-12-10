
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Core.UnitTests.Data
{
    [TestFixture]
    public class GivenAnEmptyDictionaryRepository : GivenADictionaryRepository
    {
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
        }

        [Test]
        public void WhenGettingAllItems_ThenNoItemsAreReturned()
        {
            var items = this.Repository.GetAllAsync().Result;
            Assert.AreEqual(0, items.Count);
        }

        [Test]
        public void WhenCreatingANewItem_ThenAnItemIsReturned()
        {
            var item = this.Repository.New();
            Assert.AreNotEqual(null, item);
        }

        [Test]
        public void WhenAddingAnItem_ThenOneItemIsAdded()
        {
            this.Repository.SaveAsync(this.Repository.New()).Wait();
            var count = this.Repository.GetAllAsync().Result.Count;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void WhenAddingAnItem_ThenTheSaveResultIsAdded()
        {
            var saveResult = this.Repository.SaveAsync(this.Repository.New()).Result;
            Assert.AreEqual(SaveResult.Added, saveResult);
        }

        [Test]
        public void WhenAddingAnItem_ThenTheItemIsReturned()
        {
            var item = this.Repository.New();
            this.Repository.SaveAsync(item).Wait();
            var result = this.Repository.GetByIdAsync(item.Identity).Result;
            Assert.AreEqual(item, result);
        }

        [Test]
        public void WhenAdding10Items_Then10AreReturned()
        {
            for (int i = 0;i<10;i++)
            {
                this.Repository.SaveAsync(this.Repository.New()).Wait();
            }

            var count = this.Repository.GetAllAsync().Result.Count;
            Assert.AreEqual(10, count);
        }
    }
}
