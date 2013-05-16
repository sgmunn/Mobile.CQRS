// --------------------------------------------------------------------------------------------------------------------
// <copyright file=".cs" company="sgmunn">
//   (c) sgmunn 2013  
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Mobile.CQRS
{
    /// <summary>
    /// Represents an abstract event that has occured.
    /// </summary>
    public interface IEvent : IUniqueId
    {
    }

    /// <summary>
    /// Represents a topic about a specific model
    /// </summary>
    public interface IModelTopic
    {
        Type ModelType { get; }

        Guid ModelId { get; }
    }

    /// <summary>
    /// Implements IModelTopic
    /// </summary>
    public sealed class ModelTopic : IModelTopic
    {
        public ModelTopic(Type modelType, Guid modelId)
        {
            this.ModelType = modelType;
            this.ModelId = modelId;
        }

        public Type ModelType { get; private set; }

        public Guid ModelId { get; private set; }
    }

    /// <summary>
    /// Represents the notification of an event
    /// </summary>
    public interface INotification_ 
    {
        IEvent Event { get; }
    }

    /// <summary>
    /// Represents a notification of an event for an identifiable model
    /// </summary>
    // was INotificationEvent
    public interface IModelNotification : INotification_
    {
        IModelTopic Topic { get; }
    }

    /// <summary>
    /// Implements IModelNotification
    /// </summary>
    public sealed class ModelNotification : IModelNotification
    {
        public ModelNotification(IModelTopic topic, IEvent evt )
        {
            this.Topic = topic;
            this.Event = evt;
        }

        public IModelTopic Topic { get; private set; }

        public IEvent Event { get; private set; }

        public override string ToString()
        {
            return string.Format("[Notification: Id={0}, Event={1}]", this.Topic, this.Event);
        }
    }


    // was INotificationEventBus
    /// <summary>
    /// An observable series of model notifications
    /// </summary>
    public interface IModelNotificationBus : IObservable<IModelNotification> 
    {
        void Publish(IModelNotification notification);
    }

}

namespace Mobile.CQRS.Data
{
    // was IDataChangeEvent
    public interface IModelChangeEvent : IEvent
    {
        /// <summary>
        /// Gets the model that changed 
        /// </summary>
        /// <value>The data.</value>
        object Model { get; }

        /// <summary>
        /// Gets the change that occured to the model
        /// </summary>
        /// <value>The change.</value>
        ModelChangeKind Change { get; }
    }

    // was DataChangeEvent
    public sealed class ModelChangeEvent : IModelChangeEvent
    {
        public ModelChangeEvent(object model, ModelChangeKind change)
        {
            this.Identity = Guid.NewGuid();
            this.Model = model;
            this.Change = change;
        }

        public Guid Identity { get; set; }

        public object Model { get; private set; }

        public ModelChangeKind Change { get; private set; }

        public override string ToString()
        {
            return string.Format("[ModelChange: Change={0}, Data={1}]", this.Change, this.Model);
        }
    }

    public static class Notifications
    {
        public static IModelNotification CreateModelNotification(Guid modelId, object model, ModelChangeKind change)
        {
            return new ModelNotification(new ModelTopic(model.GetType(), modelId), new ModelChangeEvent(model, change));
        }
    }



    /// <summary>
    /// Represents a process which takes an initial state and builds a projection from the events
    /// and returns a new state.
    /// </summary>
    public interface IProjection<TProjection>
    {
        // only works for 1 to 1 projections.
        // what about where we build a list of objects based on events from a single aggregate (1 to m)?
        // there is information in each event that could identify the read model, but only the projection knows
        // this.
        // in which case we really need to pass in the repo, rather than have it external to the projection
        // unless we have explicit support for managing lists.
        // kind of a bit of an abstraction for perhaps not that much benefit..



        IList<TProjection> Build(TProjection initialState, IList<IEvent> events);
    }

    /// <summary>
    /// Base class for building a projection from events for a specific read model.
    /// </summary>
    public abstract class ProjectionBuilder<TProjection> : IProjection<TProjection>
    {
        public IList<TProjection> Build(TProjection initialState, IList<IEvent> events)
        {
            this.State = initialState;

            foreach (var evt in events)
            {
                this.HandleEvent(evt);
            }

            return null;//this.State;
        }

        protected TProjection State { get; set; }

        private void HandleEvent(object someEvent)
        {
            try
            {
                // don't fail if the method doesn't exist - projections only care about certain events
                MethodExecutor.ExecuteMethod(this, someEvent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error building projection\n{0}", ex);
                throw;
            }
        }
    }

    public static class Projections
    {
        /// <summary>
        /// Builds the projection from the events using the given repository and projection.
        /// </summary>
        public static IModelNotification BuildProjection<TProjection>(IRepository<TProjection, Guid> repository, IProjection<TProjection> projection, Guid id, IList<IEvent> events)
        {
            if (events.Any())
            {
                var initialState = repository.GetById(id);
                var currentState = projection.Build(initialState, events);

                if (currentState != null)
                {
                    // repository.Save(currentState);
                    // TODO: projection - return new DataChangeEvent<TProjection>(id, currentState, initialState == null ? DataChangeKind.Added : DataChangeKind.Changed);
                }
                else
                {
                    if (initialState != null)
                    {
                        repository.DeleteId(id);
                        // TODO: projection - return new DataChangeEvent<TProjection>(id, initialState, DataChangeKind.Deleted);
                    }
                }
            }

            return null;
        }
    }

    public interface ISpecification<T>
    {
        bool IsSatisfiedBy(T sut);
    }

//    public class EligibleForDiscountSpecification : ISpecification<Customer>
//    {
//        private readonly Product _product;
//        public EligibleForDiscountSpecification(Product product)
//        {
//            _product = product;
//        }
//
//        public bool IsSatisfiedBy(Customer customer)
//        {
//            return (_product.Price < 100 && customer.CreditRating >= _product.MinimumCreditRating);
//        }
//    }

    /// <summary>
    /// Represents a query that returns instances of TState.
    /// </summary>
    public interface IQuery<TState>
    {
        IList<TState> Execute(int pageNumber, int pageSize);
        IList<TState> Execute();
    }

    public class SqlQuery<TState> : IQuery<TState>
        where TState : IUniqueId
    {
        private Func<int, int, IList<TState>> onExecute;

        public SqlQuery(object connection)
        {

        }

        public SqlQuery(object connection, Func<int, int, IList<TState>> onExecute)
        {
            this.onExecute = onExecute;
        }

        public virtual IList<TState> Execute(int page, int pageSize)
        {
            if (this.onExecute != null)
            {
                return this.onExecute(page, pageSize);
            }

            return null;
        }
        
        public IList<TState> Execute()
        {
            return this.Execute(0, 0);
        }
    }

    public interface ISubscription
    {
        // subscription is the wrong name really, more of a cursor in a feed
        IList<object> Get(int batchSize);
        // indicates which event we just processed
        void Handled(Guid eventId);
    }

    public class Test
    {
        public ISubscription Subscribe()
        {
            return null;
        }
    }

}


namespace Mobile.CQRS.Domain
{
    using Mobile.CQRS.Data;
    
    public static class Notifications
    {
        public static IModelNotification CreateNotification(Type aggregateRootType, IAggregateEvent domainEvent)
        {
            return new ModelNotification(new ModelTopic(aggregateRootType, domainEvent.AggregateId), domainEvent);
        }
    }

    /*
     * when we execute a command, the aggregate generates uncommitted events
     * the aggregate repo, when saving the uncommitted events publishes them to 
     * an event bus.
     * 
     * that event bus currently does the read model building, based on registrations of a read model to an aggregate type
     * 
     * what we need is a queue for a read model / projection and to place those events into a queue to be processed
     * we also probably need to be able to have a transaction scope around this building
     * 
     * what we need is a notification subscription mechanism, subscribing will catch us up if we haven't subscribed before
     * 
     * need to think about sync and conflicts as their requirements may influence.....
     * 
     * subscription could be a registration of what events to find and a cursor indicating where in the list the subscriber is up to
     *  -- means that there must be a repeatable order of events across aggregates.
     *  -- is this a problem in a mobile app?
     * 
     * 
     * 
     * 
     */
}
