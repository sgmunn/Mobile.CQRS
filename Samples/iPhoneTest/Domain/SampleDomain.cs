// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SampleDomain.cs" company="sgmunn">
//   (c) sgmunn 2012  
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

namespace Sample.Domain
{
    using System;
    using Mobile.CQRS.Domain;
    using Mobile.CQRS.Data.SQLite;
    using Mobile.CQRS.Domain.SQLite;
    using System.Collections.Generic;
    using Mobile.CQRS;

    public class TestDomainContext : SqlDomainContext
    {
        public TestDomainContext(SQLiteConnection connection, IAggregateManifestRepository manifest, IEventStoreRepository eventStore) 
            : base(connection, manifest, eventStore)
        {
            this.EventSerializer = new DefaultEventSerializer<EventBase>();
        }
    }

//    public class TestAggregateId : Identity
//    {
//        public TestAggregateId(Guid id) : base(id)
//        {
//        }
//    }

    public class EventSourcedRoot : AbstractAggregateRoot, IEventSourced
    {
        ////private string name;
        private decimal balance;

        public EventSourcedRoot() : base()
        {
        }
   
        public void Execute(TestCommand1 command)
        {
            Console.WriteLine("Execute TestCommand1");
            this.RaiseEvent(command.AggregateId, new TestEvent1{
                Name = command.Name,
            });
        }

        public void Apply(TestEvent1 domainEvent)
        {
            Console.WriteLine("Apply TestEvent1 {0}", domainEvent.Version);
            ////this.name = domainEvent.Name;
        }
   
        public void Execute(TestCommand2 command)
        {
            Console.WriteLine("Execute TestCommand2");

            this.RaiseEvent(command.AggregateId, new TestEvent2{
                Description = command.Description,
                Amount = command.Amount,
            });

            this.RaiseEvent(command.AggregateId, new BalanceUpdatedEvent{
                Balance = this.balance += command.Amount,
            });
        }

        public void Apply(TestEvent2 domainEvent)
        {
            Console.WriteLine("Apply TestEvent2 {0}", domainEvent.Version);
        }

        public void LoadFromEvents(IList<IAggregateEvent> events)
        {
            base.ApplyEvents(events);
        }

        public void Apply(BalanceUpdatedEvent domainEvent)
        {
            Console.WriteLine("Apply BalanceUpdatedEvent {0}", domainEvent.Balance);
            this.balance = domainEvent.Balance;
        }
    }

    public class TestSnapshot : ISnapshot
    {
        [PrimaryKey]
        public Guid Identity { get; set; }

        public int Version { get; set; }

        public string Description { get; set; }

        public string Name { get; set; }

        public decimal Balance { get; set; }

        public override string ToString()
        {
            return string.Format("snapshot {0} [{1}] {2}", this.Version, this.Name, this.Balance);
        }
    }
    
    public class SnapshotTestRoot : AbstractAggregateRoot<TestSnapshot>
    {
        public void Execute(TestCommand1 command)
        {
            Console.WriteLine("Execute TestCommand1");
            this.RaiseEvent(command.AggregateId, new TestEvent1{
                Name = command.Name,
            });
        }

        public void Apply(TestEvent1 domainEvent)
        {
            Console.WriteLine("Apply TestEvent1 {0} - {1}", domainEvent.Version, domainEvent.Name);
            this.InternalState.Name = domainEvent.Name;
        }
   
        public void Execute(TestCommand2 command)
        {
            Console.WriteLine("Execute TestCommand2");

            this.RaiseEvent(command.AggregateId, new TestEvent2{
                Description = command.Description,
                Amount = command.Amount,
            });

            this.RaiseEvent(command.AggregateId, new BalanceUpdatedEvent{
                Balance = this.InternalState.Balance += command.Amount,
            });
        }

        public void Apply(TestEvent2 domainEvent)
        {
            Console.WriteLine("Apply TestEvent2 {0}", domainEvent.Version);
        }

        public void Apply(BalanceUpdatedEvent domainEvent)
        {
            Console.WriteLine("Apply BalanceUpdatedEvent {0}", domainEvent.Balance);
            this.InternalState.Balance = domainEvent.Balance;
        }

        public override ISnapshot GetSnapshot()
        {
            var snapshot = this.InternalState;
            snapshot.Identity = this.Identity;
            snapshot.Version = this.Version;
            return snapshot;
        }
    }
}

