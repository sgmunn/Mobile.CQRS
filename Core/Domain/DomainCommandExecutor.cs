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

    public sealed class DomainCommandExecutor : ICommandExecutor 
    {
        private readonly IUnitOfWorkScope scope;

        private readonly IAggregateRegistration registration;

        private readonly IDomainNotificationBus eventBus;

        public DomainCommandExecutor(IUnitOfWorkScope scope, IAggregateRegistration registration, IDomainNotificationBus eventBus)
        {
            if (eventBus == null)
                throw new ArgumentNullException("eventBus");
            if (registration == null)
                throw new ArgumentNullException("registration");
            if (scope == null)
                throw new ArgumentNullException("scope");

            this.scope = scope;
            this.registration = registration;
            this.eventBus = eventBus;
        }
 
        public void Execute(IList<IAggregateCommand> commands, int expectedVersion)
        {
            if (commands.Count == 0)
            {
                return;
            }

            // readModelBuilderBus will publish events to a bus that will build read models and then pass events off to
            // the real eventBus
            var readModelBuilderBus = new ReadModelBuildingEventBus(this.registration.ImmediateReadModels(this.scope), this.eventBus);

            // holds the events that were raised from the aggregate and will push them to the read model building bus
            var aggregateEvents = new UnitOfWorkEventBus(readModelBuilderBus);

            // subscribe to the changes in the aggregate and publish them to aggregateEvents
            var aggregateRepo = new AggregateRepository(registration.New, this.scope.GetRegisteredObject<IEventStore>(), scope.GetRegisteredObject<ISnapshotRepository>());
            var subscription = aggregateRepo.Changes.Subscribe(aggregateEvents.Publish);
            this.scope.Add(new UnitOfWorkDisposable(subscription));

            // add them in this order so that aggregateEvents >> readModelBuilderBus >> read model builder >> eventBus
            this.scope.Add(aggregateEvents);
            this.scope.Add(readModelBuilderBus);

            var cmd = new CommandExecutor(aggregateRepo);
            cmd.Execute(commands.ToList(), expectedVersion);

            // enqueue pending commands
            this.EnqueueCommands(this.scope.GetRegisteredObject<IPendingCommandRepository>(), commands);

            // enqueue read models to be built - for non-immediate read models
            this.EnqueueReadModels(this.scope.GetRegisteredObject<IReadModelQueueProducer>(), registration.AggregateType.Name, aggregateEvents.GetEvents().Select(x => x.Event).OfType<IAggregateEvent>().ToList());
        }
        
        private void EnqueueCommands(IPendingCommandRepository commandRepository, IList<IAggregateCommand> commands)
        {
            if (commandRepository != null)
            {
                foreach (var cmd in commands)
                {
                    commandRepository.StorePendingCommand(cmd);
                }
            }
        }
        
        private void EnqueueReadModels(IReadModelQueueProducer queue, string aggregateType, IList<IAggregateEvent> events)
        {
            if (queue != null)
            {
                var groupedEvents = from evt in events
                    group evt by evt.AggregateId into g
                select new { RootId = g.Key, Version = g.Min(x => x.Version)};

                foreach (var g in groupedEvents)
                {
                    var workItem = queue.Enqueue(g.RootId, aggregateType, g.Version);
                    if (workItem != null)
                    {
                        var topic = new DomainTopic(this.registration.AggregateType, g.RootId);
                        this.eventBus.Publish(new DomainNotification(topic, workItem));
                    }
                }
            }
        }
    }
}