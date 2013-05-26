// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DomainCommandExecutor_T.cs" company="sgmunn">
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

    public class DomainCommandExecutor<T> : ICommandExecutor 
        where T : class, IAggregateRoot, new()
    {
        private readonly Func<IUnitOfWorkScope> scopeFactory;

        private readonly AggregateRepository<T> repository;

        private readonly IList<IReadModelBuilder> readModelBuilders;

        private readonly IDomainNotificationBus eventBus;

        public DomainCommandExecutor(Func<IUnitOfWorkScope> scopeFactory, AggregateRepository<T> repository, IList<IReadModelBuilder> readModelBuilders, IDomainNotificationBus eventBus)
        {
            if (scopeFactory == null)
                throw new ArgumentNullException("scopeFactory");
            if (eventBus == null)
                throw new ArgumentNullException("eventBus");
            if (readModelBuilders == null)
                throw new ArgumentNullException("readModelBuilders");
            if (repository == null)
                throw new ArgumentNullException("repository");

            this.eventBus = eventBus;
            this.repository = repository;
            this.readModelBuilders = readModelBuilders;
            this.scopeFactory = scopeFactory;
        }
 
        public void Execute(IList<IAggregateCommand> commands, int expectedVersion)
        {
            var lifetime = new CompositeDisposable();
            using (lifetime)
            {
                var scope = this.scopeFactory();
                lifetime.Add(scope);

                // create a bus that will build read models on commit of the events that are passed to it
                // it will pass events that it captured plus any events from updating the read models to eventBus
                var readModelBuilderBus = new ReadModelBuildingEventBus<T>(this.readModelBuilders, this.eventBus);

                // capture the aggregate's events and pipe them into the read model builder when aggregateEvents is committed
                var aggregateEvents = new UnitOfWorkEventBus(readModelBuilderBus);

                // subscribe to the changes in the aggregate and publish them to aggregateEvents
                lifetime.Add(this.repository.Changes.Subscribe(aggregateEvents.Publish));

                // create a unit of work repo to wrap the real aggregate repository, also caches the aggregate for multiple command executions
                var repo = new UnitOfWorkRepository<T>(this.repository);

                // add them in this order,
                // on commit of the scope, the repo will attempt to commit the aggregate's events and will then raise
                // the events which will be passed to aggregateEvents
                // then aggregateEvents will be committed which will pass them to readModelBuilderBus
                // readModelBuilderBus will then be commited, which will then build read models and add more events to busBuffer
                // finally, if we get out of the scope, busBUffer will publish all the caught events
                scope.Add(repo);
                scope.Add(aggregateEvents);
                scope.Add(readModelBuilderBus);
            
                // execute the commands
                var cmd = new CommandExecutor<T>(repo);
                cmd.Execute(commands.ToList(), expectedVersion);
            
                // commit the changes made to the aggregate repo - eventstore and snapshots
                // this triggers off the read models to be updated from events
                scope.Commit();
            }
        }
    }
}