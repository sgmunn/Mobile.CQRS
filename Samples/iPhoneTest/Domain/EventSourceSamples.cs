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

namespace Sample.Domain
{
    using System;
    using System.Reflection;
    using Mobile.CQRS.Domain;
    using Mobile.CQRS.Domain.SQLite;
    using Mobile.CQRS.Data.SQLite;
    using Mobile.CQRS;

    public static class EventSourceSamples
    {
        public static Guid TestId = new Guid("{c239587e-c8bc-4654-9f28-6a79a7feb12a}");

        public static IDomainContext GetDomainContext()
        {
            KnownTypes.RegisterEvents(Assembly.GetExecutingAssembly());

            var manifest = new AggregateManifestRepository(EventSourcedDB.Main);

            var eventSerializer = new DataContractSerializer<EventBase>(TypeHelpers.FindSerializableTypes(typeof(EventBase), Assembly.GetCallingAssembly()));

            var eventStore = new EventStore(EventSourcedDB.Main, eventSerializer);

            var context = new TestDomainContext(EventSourcedDB.Main, manifest, eventStore);
//            context.EventBus.Subscribe((x) => Console.WriteLine("domain bus event {0}", x));

            // registrations
            context.RegisterBuilder<EventSourcedRoot>((c) => 
                 new TransactionReadModelBuilder(new SqlRepository<TransactionDataContract>(EventSourcedDB.Main)));

            return context;
        }

        public static void DoTest1()
        {
            var context = GetDomainContext();

            var id = TestId;

            var executor = context.NewCommandExecutor<EventSourcedRoot>();

            executor.Execute(new TestCommand1 
                { 
                    AggregateId = id,
                    Name = Guid.NewGuid().ToString().Substring(0, 8),
                });
        }

        public static void DoTest2()
        {
            var context = GetDomainContext();

            var id = TestId;

            var executor = context.NewCommandExecutor<EventSourcedRoot>();

            executor.Execute(new TestCommand2 
                { 
                    AggregateId = id,
                    Description = Guid.NewGuid().ToString().Substring(0, 8),
                    Amount = 100,
                });
        }
    }
}

