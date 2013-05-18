// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TestSpecification.cs" company="sgmunn">
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
    using NUnit.Framework;

    public abstract class TestSpecification<TResult>
    {
        protected TestSpecification()
        {
            this.Givens = new List<object>();
        }

        public List<object> Givens { get; private set; }

        public TResult TestResult { get; private set; }

        public abstract IEnumerable<object> Given();

        public abstract TResult When();

        [SetUp]
        public virtual void SetUp()
        {
            this.Givens.AddRange(this.Given());
            this.TestResult = this.When();
        }
        
        [TearDown]
        public virtual void TearDown()
        {
            this.Givens.Clear();
            this.TestResult = default(TResult);
        }
    }
}