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
    using Mobile.CQRS.Data;
    using Mobile.CQRS.Reactive;

    public class DomainCommandExecutor<T> : ICommandExecutor<T> 
        where T : class, IAggregateRoot, new()
    {
        private readonly IDomainContext context;
        
        public DomainCommandExecutor(IDomainContext context)
        {
            this.context = context;
        }
        
        public void Execute(IAggregateCommand command)
        {
            this.Execute(new[] { command }, 0);
        }
        
        public void Execute(IList<IAggregateCommand> commands)
        {
            this.Execute(commands, 0);
        }
        
        public void Execute(IAggregateCommand command, int expectedVersion)
        {
            this.Execute(new[] { command }, expectedVersion);
        }
 
        public void Execute(IList<IAggregateCommand> commands, int expectedVersion)
        {
            // TODO: ideally what we want is to have the read models built after the commands have been execute
            // committing the aggregate repo is what will trigger the read models to be built

            // create a unit of work eventbus to capture events
            var busBuffer = new UnitOfWorkEventBus(this.context.EventBus);
            using (busBuffer)
            {
                var scope = this.context.BeginUnitOfWork();
                using (scope)
                {
                    // create a bus that will build read models on commit of the events that are passed to it
                    // it will pass events that it captured plus any events from updating the read models to busBuffer
                    var readModelBuilderBus = new ReadModelBuildingEventBus<T>(this.context.GetReadModelBuilders<T>(), busBuffer);

                    // capture the aggregate's events and pipe them into the read model builder when it is committed
                    var aggregateEvents = new UnitOfWorkEventBus(readModelBuilderBus);

                    // create a unit of work repo to wrap the real aggregate repository
                    var repo = new UnitOfWorkRepository<T>(this.context.GetAggregateRepository<T>(aggregateEvents));

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
                    foreach (var command in commands.ToList())
                    {
                        cmd.Execute(command, expectedVersion);
                    }
                
                    // commit the changes made to the aggregate repo - eventstore and snapshots
                    // this triggers off the read models to be updated from read models
                    scope.Commit();
                }

                // now publish the events that we captured from the aggregate
                busBuffer.Commit();

                // here is where we would trigger eventual consistency read models
            }
        }
    }
}