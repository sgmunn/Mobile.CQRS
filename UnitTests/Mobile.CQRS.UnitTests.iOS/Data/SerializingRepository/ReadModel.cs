// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ReadModel.cs" company="sgmunn">
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
using Mobile.CQRS.Data;
using System.Runtime.Serialization;

namespace Mobile.CQRS.Core.UnitTests.Data
{
    [DataContract(Name = "ReadMode", Namespace = "urn:ReadModel")]
    public class ChildReadModel
    {
        [DataMember]
        public string Value1 { get; set; }

        [DataMember]
        public int Value2 { get; set; }
    }

    [DataContract(Name = "ReadModel", Namespace = "urn:ReadModel")]
    public class ReadModel : IUniqueId
    {
        [DataMember]
        public Guid Identity { get; set; }

        [DataMember]
        public string Value1 { get; set; }

        [DataMember]
        public int Value2 { get; set; }

        [DataMember]
        public DateTime Value3 { get; set; }

        [DataMember]
        public ChildReadModel Child { get; set; }
    }

    public class SerializedReadModel : ISerializedReadModel
    {
        public Guid Identity { get; set; }

        public string ObjectData { get; set; }
        
        public string Value1 { get; set; }
        
        public string Value2 { get; set; }
    }
}