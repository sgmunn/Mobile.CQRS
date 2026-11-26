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
    
    /// <summary>Transactional outbox; use the event-store connection and scope.</summary>
    public sealed class ReadModelBuilderQueue : IReadModelQueue, IReadModelQueueProducer
    {
        private readonly SQLiteConnection connection;

        public ReadModelBuilderQueue(SQLiteConnection connection)
        {
            this.connection = connection;
            connection.CreateTable<ReadModelWorkItem>();
        }

        public ReadModelBuilderQueue(IUnitOfWorkScope scope)
            : this(scope.GetRegisteredObject<SQLiteConnection>()) { }

        public IReadModelWorkItem Enqueue(Guid aggregateId, string aggregateType, int fromVersion)
        {
            if (!this.connection.IsInTransaction)
                throw new InvalidOperationException("Enqueue requires the event-store transaction.");
            if (fromVersion < 0) throw new ArgumentOutOfRangeException(nameof(fromVersion));
            var existing = this.connection.Find<ReadModelWorkItem>(aggregateId);
            var item = new ReadModelWorkItem {
                Identity = aggregateId,
                AggregateType = aggregateType,
                FromVersion = existing == null ? fromVersion : Math.Min(existing.FromVersion, fromVersion),
                DeliveryId = Guid.NewGuid(),
            };
            if (existing == null) this.connection.Insert(item);
            else this.connection.Update(item);
            return item;
        }

        public void Dequeue(IReadModelWorkItem workItem)
        {
            if (!this.connection.IsInTransaction)
                throw new InvalidOperationException("Acknowledgement requires a transaction.");
            // Never acknowledge a newer append or rebuild that replaced this delivery.
            this.connection.Execute("delete from ReadModelWorkItem where Identity = ? and DeliveryId = ?",
                workItem.Identity, workItem.DeliveryId);
        }

        public IList<IReadModelWorkItem> Peek(int batchSize)
        {
            return this.connection.Table<ReadModelWorkItem>().OrderBy(x => x.Identity)
                .Take(batchSize).Cast<IReadModelWorkItem>().ToList();
        }
    }
}
