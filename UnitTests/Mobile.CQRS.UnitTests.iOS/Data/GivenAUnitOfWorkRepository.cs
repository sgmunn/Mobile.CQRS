
using System;

namespace Mobile.CQRS.Core.UnitTests.Data
{
    public class GivenAUnitOfWorkRepository : GivenADictionaryRepository
    {
        public UnitOfWorkRepository<IdTest> UnitOfWork { get; private set; }

        public override void SetUp()
        {
            base.SetUp();
            this.UnitOfWork = new UnitOfWorkRepository<IdTest>(this.Repository);
        }
    }
}
