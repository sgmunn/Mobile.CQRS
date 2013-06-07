//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="EventSourceSamples.cs" company="sgmunn">
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
using Mobile.CQRS.Serialization;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.SQLite.Domain;

namespace Sample.Domain
{
    using System;
    using System.Reflection;
    using Mobile.CQRS.Domain;
    using Mobile.CQRS.Reactive;
    

    public static class EventSourceSamples
    {
        public static Guid TestId = new Guid("{c239587e-c8bc-4654-9f28-6a79a7feb12a}");

        public static IDomainContext GetDomainContext()
        {
            var eventSerializer = new DataContractSerializer<EventBase>(TypeHelpers.FindSerializableTypes(typeof(EventBase), Assembly.GetCallingAssembly()));

            var context = new EventSourcedDomainContext(EventSourcedDB.SampleDatabasePath(), ReadModelDB1.SampleDatabasePath(), eventSerializer);
            context.EventBus.Subscribe((x) => Console.WriteLine("domain bus event {0}", x));

            var registration = AggregateRegistration.ForType<EventSourcedRoot>()
                .WithImmediateReadModel(c => new TransactionReadModelBuilder(new SqlRepository<TransactionDataContract>(EventSourcedDB.Main, "TestId")));
            context.Register(registration);

            return context;
        }

        public static void DoTest1()
        {
            var context = GetDomainContext();

            var id = TestId;

            context.Execute<EventSourcedRoot>(new TestCommand1 
                { 
                    AggregateId = id,
                    Name = Guid.NewGuid().ToString().Substring(0, 8),
                });
        }

        public static void DoTest2()
        {
            var context = GetDomainContext();

            var id = TestId;

            context.Execute<EventSourcedRoot>(new TestCommand2 
                { 
                    AggregateId = id,
                    Description = Guid.NewGuid().ToString().Substring(0, 8),
                    Amount = 100,
                });
        }

        public static void DoDeleteTest()
        {
            //var context = GetDomainContext();

            new TransactionReadModelBuilder(new SqlRepository<TransactionDataContract>(EventSourcedDB.Main, "TestId")).DeleteForAggregate(TestId);
        }
    }
}

