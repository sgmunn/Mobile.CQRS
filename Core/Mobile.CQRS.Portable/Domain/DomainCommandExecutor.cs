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
    using System.Threading.Tasks;
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
 
        public async Task ExecuteAsync(IList<IAggregateCommand> commands, int expectedVersion)
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

            // Note, we have a subsciption that is async void, but this isn't an issue here as we know that all the publish does
            // is to add the event to a queue because it being a UnitOfWorkEventBus. If the bus was going to do something with
            // the event, it might become an issue.
            var subscription = aggregateRepo.Changes.Subscribe(async (evt) => await aggregateEvents.PublishAsync(evt));

            this.scope.Add(new UnitOfWorkDisposable(subscription));

            // add them in this order so that aggregateEvents >> readModelBuilderBus >> read model builder >> eventBus
            this.scope.Add(aggregateEvents);
            this.scope.Add(readModelBuilderBus);

            var cmd = new CommandExecutor(aggregateRepo);
            await cmd.ExecuteAsync(commands.ToList(), expectedVersion).ConfigureAwait(false);

            // enqueue pending commands
            await this.EnqueueCommandsAsync(this.scope.GetRegisteredObject<IPendingCommandRepository>(), commands).ConfigureAwait(false);

            // enqueue read models to be built - for non-immediate read models
            var typeName = AggregateRootBase.GetAggregateTypeDescriptor(registration.AggregateType);
            await this.EnqueueReadModelsAsync(this.scope.GetRegisteredObject<IReadModelQueueProducer>(), typeName, aggregateEvents.GetEvents().Select(x => x.Event).OfType<IAggregateEvent>().ToList()).ConfigureAwait(false);
        }
        
        private async Task EnqueueCommandsAsync(IPendingCommandRepository commandRepository, IList<IAggregateCommand> commands)
        {
            if (commandRepository != null)
            {
                foreach (var cmd in commands)
                {
                    await commandRepository.StorePendingCommandAsync(cmd).ConfigureAwait(false);
                }
            }
        }
        
        private async Task EnqueueReadModelsAsync(IReadModelQueueProducer queue, string aggregateType, IList<IAggregateEvent> events)
        {
            if (queue != null)
            {
                var groupedEvents = from evt in events
                    group evt by evt.AggregateId into g
                select new { RootId = g.Key, Version = g.Min(x => x.Version)};

                foreach (var g in groupedEvents)
                {
                    var workItem = await queue.EnqueueAsync(g.RootId, aggregateType, g.Version).ConfigureAwait(false);
                    if (workItem != null)
                    {
                        var topic = new DomainTopic(this.registration.AggregateType, g.RootId);
                        await this.eventBus.PublishAsync(new DomainNotification(topic, workItem)).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}