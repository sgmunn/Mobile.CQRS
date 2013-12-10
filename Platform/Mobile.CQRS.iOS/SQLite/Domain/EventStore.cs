// --------------------------------------------------------------------------------------------------------------------
// <copyright file="EventStore.cs" company="sgmunn">
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

namespace Mobile.CQRS.SQLite.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mobile.CQRS.Serialization;
    using Mobile.CQRS.Domain;
    using System.Threading.Tasks;

    // TODO: event store doesn't actually do anything async
    public sealed class EventStore : SqlRepository<AggregateEvent>, IMergableEventStore
    {
        private readonly ISerializer<IAggregateEvent> serializer;

        private readonly IAggregateIndexRepository index;

        public EventStore(SQLiteConnection connection, ISerializer<IAggregateEvent> serializer) : base(connection)
        {
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            this.serializer = serializer;
            this.index = new AggregateIndexRepository(connection);

            connection.CreateTable<AggregateEvent>();
        }
        
        public async Task SaveEventsAsync(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion)
        {
            if (events.Count == 0)
            {
                return;
            }

            var serializedEvents = events.Select(evt => new AggregateEvent{
                AggregateId = aggregateId, 
                Identity = evt.Identity,
                Version = evt.Version,
                AggregateType = evt.AggregateType,
                EventData = this.serializer.SerializeToString(evt),
            }).ToList();

            var newVersion = events[events.Count - 1].Version;
            await this.index.UpdateIndexAsync(aggregateId, expectedVersion, newVersion).ConfigureAwait(false);

            foreach (var evt in serializedEvents)
            {
                this.Connection.Insert(evt);
            }
        }

        public async Task MergeEventsAsync(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion, int fromVersion)
        {
            var serializedEvents = events.Select(evt => new AggregateEvent{
                AggregateId = aggregateId, 
                Identity = evt.Identity,
                Version = evt.Version,
                AggregateType = evt.AggregateType,
                EventData = this.serializer.SerializeToString(evt),
            }).ToList();

            var newVersion = events[events.Count - 1].Version;
            await this.index.UpdateIndexAsync(aggregateId, expectedVersion, newVersion).ConfigureAwait(false);

            this.Connection.Execute(string.Format("delete from AggregateEvent where AggregateId = ? and Version > {0}", fromVersion), aggregateId);

            foreach (var evt in serializedEvents)
            {
                this.Connection.Insert(evt);
            }
        }

        public Task<IList<IAggregateEvent>> GetAllEventsAsync(Guid rootId)
        {
            return Task.Factory.StartNew<IList<IAggregateEvent>>(() => {
                var events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId).OrderBy(x => x.Version).ToList();
                return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
            });
        }

        public Task<IList<IAggregateEvent>> GetEventsAfterVersionAsync(Guid rootId, int version)
        {
            return Task.Factory.StartNew<IList<IAggregateEvent>>(() =>
            {
                var events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId && x.Version > version).OrderBy(x => x.Version).ToList();
                return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
            });
        }

        public Task<IList<IAggregateEvent>> GetEventsUpToVersionAsync(Guid rootId, int version)
        {
            return Task.Factory.StartNew<IList<IAggregateEvent>>(() =>
            {
                var events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId && x.Version <= version).OrderBy(x => x.Version).ToList();
                return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
            });
        }

        public Task<int> GetCurrentVersionAsync(Guid rootId)
        {
            return Task.Factory.StartNew<int>(() =>
            {
                var result = this.Connection.Table<AggregateEvent>()
                .Where(x => x.AggregateId == rootId)
                    .OrderByDescending(x => x.Version)
                    .Take(1).ToList();

                return result.Select(x => x.Version).FirstOrDefault();
            });
        }
    }
}