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
    using System.Threading.Tasks;
    using Mobile.CQRS.Domain;

    /// <summary>
    /// Implements enqueing and dequeing a read model builder queue. It uses a lock to ensure that enqueing and dequeing don't 
    /// cause a sqlite busy exception.
    /// </summary>
    public sealed class ReadModelBuilderQueue : IReadModelQueue, IReadModelQueueProducer 
    {
        private readonly SqlRepository<ReadModelWorkItem> repository;

        private readonly object locker = new object();

        public ReadModelBuilderQueue(SQLiteConnection connection)
        {
            this.repository = new SqlRepository<ReadModelWorkItem>(connection);
            connection.CreateTable<ReadModelWorkItem>();
        }
        
        public ReadModelBuilderQueue(IUnitOfWorkScope scope)
        {
            var connection = scope.GetRegisteredObject<SQLiteConnection>();
            this.repository = new SqlRepository<ReadModelWorkItem>(connection);
            this.repository.Connection.CreateTable<ReadModelWorkItem>();
        }

        public async Task<IReadModelWorkItem> EnqueueAsync(Guid aggregateId, string aggregateType, int fromVersion)
        {
            var workItem = new ReadModelWorkItem {
                AggregateType = aggregateType,
                Identity = aggregateId,
                FromVersion = fromVersion
            };

            Monitor.Enter(this.locker);
            try
            {
                var existingItem = await this.repository.GetByIdAsync(workItem.Identity);
                if (existingItem != null && existingItem.FromVersion < workItem.FromVersion)
                {
                    return null;
                }

                await this.repository.SaveAsync((ReadModelWorkItem)workItem);
            }
            finally
            {
                Monitor.Exit(this.locker);
            }

            return workItem;
        }

        public async Task DequeueAsync(IReadModelWorkItem workItem)
        {
            Monitor.Enter(this.locker);
            try
            {
                const string deleteCmd = "delete from ReadModelWorkItem where Identity = ? and FromVersion = {0}";
                await Task.Factory.StartNew(() => {
                    this.repository.Connection.Execute(string.Format(deleteCmd, workItem.FromVersion), workItem.Identity);
                });
            }
            finally
            {
                Monitor.Exit(this.locker);
            }
        }

        public async Task<IList<IReadModelWorkItem>> PeekAsync(int batchSize)
        {
            if (Monitor.TryEnter(this.locker))
            {
                try
                {
                    return await Task.Factory.StartNew<IList<IReadModelWorkItem>>(() => {
                        return this.repository.Connection.Table<ReadModelWorkItem>().Take(batchSize).Cast<IReadModelWorkItem>().ToList();
                    });
                }
                finally
                {
                    Monitor.Exit(this.locker);
                }
            }

            return new List<IReadModelWorkItem>();
        }
    }
}