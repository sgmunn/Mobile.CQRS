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

// TODO: serialization support for complex read models and snapshots
// TODO: thread safe event bus - need to be able to publish to it from any thread - Subject needs to be thread safe
// TODO: event sourcing and snapshot support in the same aggregate
// TODO: verify the aggregate manifest and see if we can have an easier name
// TODO: we want an ISnapshotContract like we have for events that serialises, this would be opt-in and the aggregate would have to serialize??
// TODO: view model with support for loading changes while being editing

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

}
