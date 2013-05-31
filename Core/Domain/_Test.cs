using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;


using System.Reflection;
using System.Collections.Concurrent;
using Mobile.CQRS.SQLite.Domain;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.Serialization;
using System.Threading;
using System.Threading.Tasks;


namespace Mobile.CQRS.Domain
{
    using Mobile.CQRS.Reactive;

    public interface IReadModelBuilderFaulted : IEvent
    {
        Exception Fault { get; }
    }
    
    public sealed class ReadModelBuilderFaultedEvent : IReadModelBuilderFaulted
    {
        public ReadModelBuilderFaultedEvent(Guid identity, Exception ex)
        {
            this.Identity = identity;
            this.Fault = ex;
        }

        public Guid Identity { get; set; }
        public Exception Fault { get; set; }
    }

    public sealed class ReadModelBuilderAgent
    {
        private readonly IDomainContext context;

        private readonly List<IAggregateRegistration> registrations;
        
        private readonly int maxItems;

        private readonly Func<IUnitOfWorkScope, IReadModelQueue> queueFactory;

        private readonly Func<IUnitOfWorkScope> scopeFactory;

        private CancellationTokenSource cancel;

        private IDisposable eventBusSubscription;
        
        private int locker;

        public ReadModelBuilderAgent(IDomainContext context, 
                                     List<IAggregateRegistration> registrations, Func<IUnitOfWorkScope> scopeFactory, Func<IUnitOfWorkScope, IReadModelQueue> queueFactory)
        {
            this.context = context;
            this.registrations = registrations;
            this.queueFactory = queueFactory;
            this.scopeFactory = scopeFactory;
            this.maxItems = 50;
        }

        public bool IsStarted { get; private set; }

        public bool IsFaulted { get; private set; }
        
        public Exception Fault { get; private set; }

        public void Start()
        {
            if (!this.IsStarted)
            {
                this.IsStarted = true;
                this.cancel = new CancellationTokenSource();
                this.eventBusSubscription = this.context.EventBus.Subscribe(this.HandleDomainEvent);
                this.ProcessQueue();
            }
        }

        public void Stop()
        {
            if (this.IsStarted)
            {
                this.IsStarted = false;
                this.cancel.Cancel();
                this.eventBusSubscription.Dispose();
            }
        }

        public void Trigger()
        {
            this.ProcessQueue();
        }

        private void HandleDomainEvent(INotification notification)
        {
            if (notification.Event is IReadModelWorkItem)
            {
                this.ProcessQueue();
            }
        }

        private void ProcessQueue()
        {
            if (this.IsFaulted)
            {
                return;
            }

            var process = Task.Factory.StartNew(() => {
                if (Interlocked.Exchange(ref this.locker, 1) == 0)
                {
                    try
                    {
                        this.DoProcessQueue();
                    }
                    catch (Exception ex)
                    {
                        this.IsFaulted = true;
                        this.Fault = ex;
                    }
                    finally
                    {
                        Interlocked.Exchange(ref this.locker, 0);
                    }
                }
            });
        }

        private void DoProcessQueue()
        {
            // begin read model transaction scope
            // get events for workitem.AggregateId
            // pass events to read model
            // commit

            // we might see a work item for an aggregate twice in a row
            // especially if we poke the queue when we receive events on 
            // the event bus
            // in fact we can subscibe to events on the event bus and do our own poking

            var doMore = true;
            while (doMore)
            {
                var scope = this.scopeFactory();
                using (scope)
                {
                    var queue = this.queueFactory(scope);
                    var workItems = queue.Peek(this.maxItems);
                    var bus = new UnitOfWorkEventBus(this.context.EventBus);
                    scope.Add(bus);

                    foreach (var workItem in workItems)
                    {
                        var registration = this.registrations.FirstOrDefault(x => x.AggregateType.Name == workItem.AggregateType);
                        if (registration != null)
                        {
                            try
                            {

                                this.ProcessWorkItem(scope, registration, bus, workItem);

                                queue.Dequeue(workItem);
                            }
                            catch (Exception ex)
                            {
                                var topic = new DomainTopic(registration.AggregateType, workItem.Identity);
                                this.context.EventBus.Publish(new DomainNotification(topic, new ReadModelBuilderFaultedEvent(workItem.Identity, ex)));
                                throw;
                            }
                        }
                    }

                    scope.Commit();
                    doMore = workItems.Count == this.maxItems;
                }
            }
        }

        private void ProcessWorkItem(IUnitOfWorkScope scope, IAggregateRegistration registration, IDomainNotificationBus bus, IReadModelWorkItem workItem)
        {
            var eventScope = this.context.BeginUnitOfWork();
            using (eventScope)
            {
                var events = eventScope.EventStore.GetEventsAfterVersion(workItem.Identity, workItem.FromVersion - 1).ToList();
                var builders = registration.DelayedReadModels(scope);
                foreach (var builder in builders)
                {
                    // TODO: if version 0 then do a rebuild
                    if (workItem.FromVersion == 0)
                    {
                        builder.DeleteForAggregate(workItem.Identity);
                    }

                    // we need to get the events from the event store
                    var builderEvents = builder.Process(events);
                    foreach (var evt in builderEvents)
                    {
                        bus.Publish(evt);
                    }
                }
            }
        }
    }
}