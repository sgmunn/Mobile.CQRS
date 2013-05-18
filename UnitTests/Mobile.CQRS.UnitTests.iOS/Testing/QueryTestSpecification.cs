// --------------------------------------------------------------------------------------------------------------------
// <copyright file="QueryTestSpecification.cs" company="sgmunn">
//   (c) sgmunn 2013  
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//   documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//   the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//   to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//   the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//   THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//   CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//   IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace MonoKit.Testing
{
    using System;
    using System.Collections.Generic;

    // sample usage
    //    public static class SomeQuery
    //    {
    //        public static ITestSpecification Spec1 = new QueryTestSpecification<object, object> {
    //            Given = () => new object(),
    //            Name = "Spec 1",
    //            When = x => x.ToString(),
    //            Then = {
    //                x => x.ShouldBe(null, "message 1"),
    //                x => x.ShouldBe(null),
    //            }
    //        };
    //
    //    }
    //
    //    [TestFixture]
    //    public class GivenTest2 
    //    {
    //        [Test]
    //        public void ThenHappy()
    //        {
    //            SomeQuery.Spec1.Execute();
    //        }
    //    }

    public sealed class QueryTestSpecification<TGiven, TResult> : ITypedTestSpecification<TGiven, TResult>
    {
        public QueryTestSpecification()
        {
            this.Then = new List<Action<TResult>>();
        }

        public string Name { get; set; }

        public Func<TGiven> Given { get; set; }
        public Func<TGiven, TResult> When { get; set; }
        public IList<Action<TResult>> Then { get; set; }

        public void Execute()
        {
            var given = this.Given();
            var result = this.When(given);

            foreach (var assertion in this.Then)
            {
                assertion(result);
            }
        }
    }
}