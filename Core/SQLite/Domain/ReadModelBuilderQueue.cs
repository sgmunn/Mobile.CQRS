// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ReadModelBuilderQueue.cs" company="sgmunn">
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

namespace Mobile.CQRS.SQLite.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Mobile.CQRS.Domain;
    
    public sealed class ReadModelBuilderQueue : IReadModelQueue, IReadModelQueueProducer 
    {
        private readonly SqlRepository<ReadModelWorkItem> repository;

        public ReadModelBuilderQueue(SQLiteConnection connection)
        {
            this.repository = new SqlRepository<ReadModelWorkItem>(connection);
            connection.CreateTable<ReadModelWorkItem>();
        }
        
        public ReadModelBuilderQueue(IUnitOfWorkScope scope)
        {
            // TODO: fix this
            if (scope is SqlUnitOfWorkScope)
            {
                var sqlScope = scope as SqlUnitOfWorkScope;
                var connection = sqlScope.Connection;
                this.repository = new SqlRepository<ReadModelWorkItem>(connection);
                this.repository.Connection.CreateTable<ReadModelWorkItem>();
            }
        }

        public IReadModelWorkItem Enqueue(Guid aggregateId, string aggregateType, int fromVersion)
        {
            var workItem = new ReadModelWorkItem {
                AggregateType = aggregateType,
                Identity = aggregateId,
                FromVersion = fromVersion
            };

            Monitor.Enter(this.repository.Connection);
            try
            {
                var existingItem = this.repository.GetById(workItem.Identity);
                if (existingItem != null && existingItem.FromVersion < workItem.FromVersion)
                {
                    return null;
                }

                this.repository.Save((ReadModelWorkItem)workItem);
            }
            finally
            {
                Monitor.Exit(this.repository.Connection);
            }

            return workItem;
        }

        public void Dequeue(IReadModelWorkItem workItem)
        {
            Monitor.Enter(this.repository.Connection);
            try
            {
                const string deleteCmd = "delete from ReadModelWorkItem where Identity = ? and FromVersion = {0}";
                this.repository.Connection.Execute(string.Format(deleteCmd, workItem.FromVersion), workItem.Identity);
            }
            finally
            {
                Monitor.Exit(this.repository.Connection);
            }
        }

        public IList<IReadModelWorkItem> Peek(int batchSize)
        {
            if (Monitor.TryEnter(this.repository.Connection))
            {
                try
                {
                    return this.repository.Connection.Table<ReadModelWorkItem>().Take(batchSize).Cast<IReadModelWorkItem>().ToList();
                }
                finally
                {
                    Monitor.Exit(this.repository.Connection);
                }
            }

            return new List<IReadModelWorkItem>();
        }
    }
}