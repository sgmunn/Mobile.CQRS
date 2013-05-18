// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TestExtensions.cs" company="sgmunn">
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
    using NUnit.Framework;

    public static class TestExtensions
    {
        public static void ShouldBe<T>(this T value, T otherValue)
        {
            Assert.AreEqual(otherValue, value);
        }

        public static void ShouldBe<T>(this T value, T otherValue, string message)
        {
            Assert.AreEqual(otherValue, value, message);
        }

        public static void ShouldBeNull(this object value)
        {
            Assert.IsNull(value);
        }
        
        public static void ShouldBeNull(this object value, string message)
        {
            Assert.IsNull(value, message);
        }
        
        public static void ShouldNotBeNull(this object value)
        {
            Assert.IsNotNull(value);
        }

        public static void ShouldNotBeNull(this object value, string message)
        {
            Assert.IsNotNull(value, message);
        }
    }
}