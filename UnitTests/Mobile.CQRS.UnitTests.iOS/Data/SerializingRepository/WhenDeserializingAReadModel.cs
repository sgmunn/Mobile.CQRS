// --------------------------------------------------------------------------------------------------------------------
// <copyright file="WhenDeserializingAReadModel.cs" company="sgmunn">
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

using System;
using NUnit.Framework;
using Mobile.CQRS.Data;
using MonoKit.Testing;
using Mobile.CQRS.Serialization;

namespace Mobile.CQRS.Core.UnitTests.Data
{
    [TestFixture]
    public class WhenDeserializingAReadModel : TestSpecification<ReadModel>
    {
        public readonly Guid TheId = Guid.NewGuid();

        public readonly DateTime TheDate = DateTime.Now.AddDays(1);

        public readonly IRepository<SerializedReadModel> Repository = new IdDictionaryRepository<SerializedReadModel>();

        public readonly DataContractSerializer<ReadModel> Serializer = new DataContractSerializer<ReadModel>(new[] { typeof(ReadModel), typeof(ChildReadModel) });

        public override System.Collections.Generic.IEnumerable<object> Given()
        {
            var readModel = new ReadModel {
                Identity = TheId,
                Value1 = "SomeValue",
                Value2 = 23,
                Value3 = TheDate,
                Child = new ChildReadModel {
                    Value1 = "Child Value",
                    Value2 = 56,
                }
            };

            var x = new SerializingRepository<ReadModel, SerializedReadModel>(this.Repository, this.Serializer);
            yield return x.Save(readModel);
        }

        public override ReadModel When()
        {
            var x = new SerializingRepository<ReadModel, SerializedReadModel>(this.Repository, this.Serializer);
            return x.GetById(TheId);
        }

        [Test]
        public void TheIdentityIsCorrect()
        {
            this.TestResult.Identity.ShouldBe(TheId);
        }
        
        [Test]
        public void Value1IsDeserialized()
        {
            this.TestResult.Value1.ShouldBe("SomeValue");
        }
        
        [Test]
        public void Value2IsDeserialized()
        {
            this.TestResult.Value2.ShouldBe(23);
        }
        
        [Test]
        public void Value3IsDeserialized()
        {
            this.TestResult.Value3.ShouldBe(TheDate);
        }
        
        [Test]
        public void ChildObjectIsDeserialized()
        {
            this.TestResult.Child.ShouldNotBeNull();
            this.TestResult.Child.Value1.ShouldBe("Child Value");
            this.TestResult.Child.Value2.ShouldBe(56);
        }
    }
}