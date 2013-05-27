// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DomainCommandExecutor.cs" company="sgmunn">
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
    using Mobile.CQRS.Reactive;

    public class DomainCommandExecutor : ICommandExecutor 
    {
        private readonly IDomainUnitOfWorkScope scope;

        private readonly IAggregateRegistration registration;

        private readonly IDomainNotificationBus eventBus;

        public DomainCommandExecutor(IDomainUnitOfWorkScope scope, IAggregateRegistration registration, IDomainNotificationBus eventBus)
        {
            this.scope = scope;
            this.registration = registration;
            this.eventBus = eventBus;
        }
 
        public void Execute(IList<IAggregateCommand> commands, int expectedVersion)
        {
            var snapshotRepo = registration.Snapshot(scope);
            var readModelBuilders = registration.ReadModels(scope);
            var aggregateRepo = new AggregateRepository(registration.New, scope.EventStore, snapshotRepo);

            // create a bus that will build read models on commit of the events that are passed to it
            // it will pass events that it captured plus any events from updating the read models to busBuffer
            var readModelBuilderBus = new ReadModelBuildingEventBus(readModelBuilders, this.eventBus);

            // capture the aggregate's events and pipe them into the read model builder when aggregateEvents is committed
            var aggregateEvents = new UnitOfWorkEventBus(readModelBuilderBus, () => {
                // on commit of the events to the read model builders, store the pending commands
                this.EnqueueCommands(scope.PendingCommands, commands);
            });


            // subscribe to the changes in the aggregate and publish them to aggregateEvents
            var subscription = aggregateRepo.Changes.Subscribe(aggregateEvents.Publish);
            scope.Add(new UnitOfWorkDisposable(subscription));


            // add them in this order,
            // on commit of the scope, aggregateRepo will attempt to commit the aggregate's events and will then raise
            // those events which will be passed to aggregateEvents
            // then aggregateEvents will be committed which will pass them to readModelBuilderBus
            // readModelBuilderBus will then be commited, which will then build read models and add more events to eventBus
            scope.Add(aggregateEvents);
            scope.Add(readModelBuilderBus);

            var cmd = new CommandExecutor(aggregateRepo);
            cmd.Execute(commands.ToList(), expectedVersion);
        }
        
        protected virtual void EnqueueCommands(IPendingCommandRepository commandRepository, IList<IAggregateCommand> commands)
        {
            if (commandRepository != null)
            {
                foreach (var cmd in commands)
                {
                    commandRepository.StorePendingCommand(cmd);
                }
            }
        }
    }
}