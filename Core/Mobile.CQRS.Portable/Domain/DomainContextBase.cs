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
    using System.Threading.Tasks;

    public abstract class DomainContextBase : IDomainContext
    {
        private readonly List<IAggregateRegistration> registrations;

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

        public IEnumerable<IAggregateRegistration> Registrations 
        {
            get
            {
                return this.registrations;
            }
        }

        public Task ExecuteAsync<T>(IAggregateCommand command) where T : IAggregateRoot
        {
            return this.ExecuteAsync<T>(new[] { command }, 0);
        }

        public Task ExecuteAsync<T>(IList<IAggregateCommand> commands) where T : IAggregateRoot
        {
            return this.ExecuteAsync<T>(commands, 0);
        }

        public Task ExecuteAsync<T>(IAggregateCommand command, int expectedVersion) where T : IAggregateRoot
        {
            return this.ExecuteAsync<T>(new[] { command }, expectedVersion);
        }

        public Task ExecuteAsync<T>(IList<IAggregateCommand> commands, int expectedVersion) where T : IAggregateRoot
        {
            return this.InternalExecuteAsync<T>(commands, expectedVersion);
        }

        public void Register(IAggregateRegistration registration)
        {
            this.registrations.Add(registration);
        }

        public virtual IUnitOfWorkScope BeginUnitOfWork()
        {
            return new InMemoryUnitOfWorkScope();
        }

        protected async virtual Task InternalExecuteAsync<T>(IList<IAggregateCommand> commands, int expectedVersion) where T : IAggregateRoot
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
                    var exec = new DomainCommandExecutor(scope, registration, busBuffer);
                    await exec.ExecuteAsync(commands, expectedVersion).ConfigureAwait(false);

                    scope.Commit();
                }

                // get events in the buffer, bundle them up and send them in one go to the background read model builder

                // finally, publish the events to the real event bus
                busBuffer.Commit();
            }
        }
    }
}