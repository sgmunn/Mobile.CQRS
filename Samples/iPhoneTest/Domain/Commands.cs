//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="Commands.cs" company="sgmunn">
//    (c) sgmunn 2012  
//
//    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//    documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//    the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//    to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//    The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//    the Software.
//
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//    THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//    CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//    IN THE SOFTWARE.
//  </copyright>
//  --------------------------------------------------------------------------------------------------------------------
//
using System.Runtime.Serialization;

namespace Sample.Domain
{
    using System;
    using Mobile.CQRS.Domain;
    
    [DataContract(Name="CommandBase", Namespace="urn:SampleDomain")]
    public class CommandBase : IAggregateCommand
    {
        public CommandBase()
        {
            this.Identity = Guid.NewGuid();
        }

        [DataMember]
        public Guid AggregateId { get; set; }
        
        [DataMember]
        public Guid Identity { get; set; }
    }

    [DataContract(Name="TestCommand1", Namespace="urn:SampleDomain")]
    public class TestCommand1 : CommandBase
    {
        [DataMember]
        public string Name { get; set; }
    }

    [DataContract(Name="TestCommand2", Namespace="urn:SampleDomain")]
    public class TestCommand2 : CommandBase
    {
        [DataMember]
        public string Description { get; set; }
 
        [DataMember]
        public decimal Amount { get; set; }
   }
}

