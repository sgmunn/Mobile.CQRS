// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DomainContextBase.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public abstract class DomainContextBase : IDomainContext
    {
        private readonly List<IAggregateRegistration> registrations;

        protected DomainContextBase()
        {
            this.EventBus = new ObservableDomainNotificationBus();
            this.registrations = new List<IAggregateRegistration>();
        }

        protected DomainContextBase(IEventStore eventStore)
        {
            this.EventBus = new ObservableDomainNotificationBus();
            this.registrations = new List<IAggregateRegistration>();
            this.EventStore = eventStore;
        }

        protected DomainContextBase(IEventStore eventStore, IDomainNotificationBus eventBus)
        {
            this.registrations = new List<IAggregateRegistration>();
            this.EventBus = eventBus;
            this.EventStore = eventStore;
        }
        
        public IDomainNotificationBus EventBus { get; protected set; }

        // note, if we change the way our unit of work scopes are handled, then we may need to provide a fn to create these as needed
        // rather than a property on the context
        public IEventStore EventStore { get; protected set; }

        public IRepository<ISyncState> SyncState { get; protected set; }

        public IPendingCommandRepository PendingCommands { get; protected set; }

        public void Execute<T>(IAggregateCommand command) where T : class, IAggregateRoot, new()
        {
            this.Execute<T>(new[] { command }, 0);
        }

        public void Execute<T>(IList<IAggregateCommand> commands) where T : class, IAggregateRoot, new()
        {
            this.Execute<T>(commands, 0);
        }

        public void Execute<T>(IAggregateCommand command, int expectedVersion) where T : class, IAggregateRoot, new()
        {
            this.Execute<T>(new[] { command }, expectedVersion);
        }

        public void Execute<T>(IList<IAggregateCommand> commands, int expectedVersion) where T : class, IAggregateRoot, new()
        {
            this.InternalExecute<T>(commands, expectedVersion);
        }

        public void Register(IAggregateRegistration registration)
        {
            this.registrations.Add(registration);
        }
        
        protected abstract object GetDataConnection();

        protected virtual IUnitOfWorkScope BeginUnitOfWork()
        {
            return new InMemoryUnitOfWorkScope();
        }

        protected virtual void EnqueueCommands(IList<IAggregateCommand> commands)
        {
            if (this.PendingCommands != null)
            {
                foreach (var cmd in commands)
                {
                    this.PendingCommands.StorePendingCommand(cmd);
                }
            }
        }

        protected virtual void InternalExecute<T>(IList<IAggregateCommand> commands, int expectedVersion) where T : class, IAggregateRoot, new()
        {
            var registration = this.registrations.FirstOrDefault(x => x.AggregateType == typeof(T));

            var snapshotRepo = registration != null ? registration.Snapshot(this.GetDataConnection()) : null;
            var readModelBuilders = new List<IReadModelBuilder>();
            if (registration != null)
            {
                readModelBuilders.AddRange(registration.ReadModels(this.GetDataConnection()));
            }

            var aggregateRepo = new AggregateRepository(() => new T(), this.EventStore, snapshotRepo);



            // create a unit of work eventbus to capture events
            var busBuffer = new UnitOfWorkEventBus(this.EventBus, () => {
                // do something with the commands, store in a command queue
                this.EnqueueCommands(commands);
            });

            using (busBuffer)
            {
                // TODO: create another bus to capture events and link to commands, when published to, store commands and pass on events
                // we need a bus that takes an action on publish 

                // the events that are published to busBuffer are 'Publish'ed during the life of the unit of work
                // in here we can push the commands that we are given to execute if we want, on first publish this will transactional

                var executor = new DomainCommandExecutor(this.BeginUnitOfWork, aggregateRepo, readModelBuilders, busBuffer);
                executor.Execute(commands, expectedVersion);

                busBuffer.Commit();
            }
        }
    }
}