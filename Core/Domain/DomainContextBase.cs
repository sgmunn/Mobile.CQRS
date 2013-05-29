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

        private IReadModelQueue readModelQueue;

        protected DomainContextBase()
        {
            this.EventBus = new ObservableDomainNotificationBus();
            this.registrations = new List<IAggregateRegistration>();
        }

        protected DomainContextBase(IDomainNotificationBus eventBus)
        {
            this.registrations = new List<IAggregateRegistration>();
            this.EventBus = eventBus;
        }
        
        public IDomainNotificationBus EventBus { get; protected set; }

        public IReadModelQueue ReadModelQueue 
        {
            get
            {
                if (this.readModelQueue == null)
                {
                    this.readModelQueue = this.GetReadModelQueue();
                }

                return this.readModelQueue;
            }
        }

        protected Lazy<IReadModelQueue> ReadModelQueueConstructor { get; set; }

        public void Execute<T>(IAggregateCommand command) where T : IAggregateRoot
        {
            this.Execute<T>(new[] { command }, 0);
        }

        public void Execute<T>(IList<IAggregateCommand> commands) where T : IAggregateRoot
        {
            this.Execute<T>(commands, 0);
        }

        public void Execute<T>(IAggregateCommand command, int expectedVersion) where T : IAggregateRoot
        {
            this.Execute<T>(new[] { command }, expectedVersion);
        }

        public void Execute<T>(IList<IAggregateCommand> commands, int expectedVersion) where T : IAggregateRoot
        {
            this.InternalExecute<T>(commands, expectedVersion);
        }

        public void Register(IAggregateRegistration registration)
        {
            this.registrations.Add(registration);
        }

        protected virtual IDomainUnitOfWorkScope BeginUnitOfWork()
        {
            return new InMemoryDomainScope();
        }

        protected virtual void InternalExecute<T>(IList<IAggregateCommand> commands, int expectedVersion) where T : IAggregateRoot
        {
            var registration = this.registrations.FirstOrDefault(x => x.AggregateType == typeof(T));
            if (registration == null)
            {
                throw new InvalidOperationException(string.Format("Unregistered aggregate type {0}", typeof(T)));
            }

            // create a unit of work eventbus to capture aggregate events and read model events and publish in one go
            using (var busBuffer = new UnitOfWorkEventBus(this.EventBus))
            {
                using (var scope = this.BeginUnitOfWork())
                {
                    var exec = new DomainCommandExecutor(scope, registration, busBuffer, this.ReadModelQueue);
                    exec.Execute(commands, expectedVersion);

                    scope.Commit();
                }

                // finally, publish the events to the real event bus
                busBuffer.Commit();
            }
        }

        protected abstract IReadModelQueue GetReadModelQueue();
    }
}