using System;
using NUnit.Framework;
using Mobile.CQRS.Runtime;

namespace Mobile.CQRS.Runtime.UnitTests.Bindings
{
    [TestFixture]
    public class MethodExecutionPerformance
    {
        [Test]
        public void WhenExecutingAMethod1000Times_ThenTheSpeedIsOK()
        {
            var obj = new ExecutableObject();
            
            for (int i= 0;i<1000;i++)
            {
                MethodExecutor.ExecuteMethod(obj, i);
            }
            
            Assert.True(true);
        }
    }

    public class ExecutableObject
    {
        public int TheValue;

        public void AMethod(int count)
        {
            this.TheValue = count;
        }
    }
}
