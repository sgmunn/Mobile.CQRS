using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

// TODO: type id on manifest
// TODO: should uow repo cache gets ?

// TODO: thread safe event bus - need to be able to publish to it from any thread - Subject needs to be thread safe
// TODO: verify the aggregate manifest and see if we can have an easier name
// TODO: Mobile.Mvvm view model with support for loading changes while being editing


using System.Reflection;

namespace Mobile.CQRS.Data
{
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

    public static class KnownTypes
    {
        public static List<Type> EventTypes = new List<Type>();

        public static void RegisterEvents(Assembly assembly)
        {
            Assembly.GetCallingAssembly();

            var eventTypes = assembly.GetTypes().Where(t => typeof(IAggregateEvent).IsAssignableFrom(t)).ToList();
            EventTypes.AddRange(eventTypes);
        }
    }


}
