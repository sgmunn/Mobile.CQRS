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

    public sealed class EventStore : SqlRepository<AggregateEvent>, IMergableEventStore
    {
        private readonly ISerializer<IAggregateEvent> serializer;

        private readonly IAggregateIndexRepository index;

        private readonly IReadModelQueueProducer outbox;

        public EventStore(SQLiteConnection connection, ISerializer<IAggregateEvent> serializer)
            : this(connection, serializer, null) { }

        public EventStore(SQLiteConnection connection, ISerializer<IAggregateEvent> serializer, IReadModelQueueProducer outbox) : base(connection)
        {
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            this.serializer = serializer;
            this.outbox = outbox;
            this.index = new AggregateIndexRepository(connection);

            connection.CreateTable<AggregateEvent>();
            connection.CreateTable<AggregateSnapshot>();
        }
        
        public bool EnqueuesReadModels => this.outbox != null;

        public void SaveEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion)
        {
            if (events.Count == 0)
            {
                return;
            }

            this.RequireOutboxTransaction();
            var serializedEvents = events.Select(evt => new AggregateEvent{
                AggregateId = aggregateId, 
                Identity = evt.Identity,
                Version = evt.Version,
                AggregateType = evt.AggregateType,
                EventData = this.serializer.SerializeToString(evt),
            }).ToList();

            var newVersion = events[events.Count - 1].Version;
            this.index.UpdateIndex(aggregateId, expectedVersion, newVersion);

            foreach (var evt in serializedEvents)
            {
                this.Connection.Insert(evt);
            }
            this.outbox?.Enqueue(aggregateId, events[0].AggregateType, events[0].Version);
        }

        public void MergeEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion, int fromVersion)
        {
            if (!this.Connection.IsInTransaction)
                throw new InvalidOperationException("History replacement and snapshot invalidation require a transaction.");
            var current = this.GetCurrentVersion(aggregateId);
            if (current != expectedVersion) throw new ConcurrencyException(aggregateId, expectedVersion, current);
            if (fromVersion < 0 || fromVersion > current) throw new ArgumentOutOfRangeException(nameof(fromVersion));
            var type = events.FirstOrDefault()?.AggregateType
                ?? this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == aggregateId).Take(1).ToList().FirstOrDefault()?.AggregateType;
            // Validate before changing the index or deleting history.
            var envelopes = SyncProtocol.Encode(events, this.serializer);
            if (events.Count > 0) SyncProtocol.Decode(aggregateId, type, envelopes, fromVersion, this.serializer);
            var newVersion = fromVersion + events.Count;
            if (newVersion == 0)
                this.Connection.Execute("delete from AggregateIndex where Identity = ?", aggregateId);
            else this.index.UpdateIndex(aggregateId, expectedVersion, newVersion);
            this.Connection.Execute("delete from AggregateEvent where AggregateId = ? and Version > ?", aggregateId, fromVersion);
            foreach (var evt in events)
                this.Connection.Insert(new AggregateEvent {
                    AggregateId = aggregateId, Identity = evt.Identity, Version = evt.Version,
                    AggregateType = evt.AggregateType, EventData = this.serializer.SerializeToString(evt),
                });
            // A same-version snapshot can still belong to the discarded branch.
            this.Connection.Execute("delete from AggregateSnapshot where Identity = ?", aggregateId);
            if (type != null) this.outbox?.Enqueue(aggregateId, type, 0);
        }

        private void RequireOutboxTransaction()
        {
            if (this.outbox != null && !this.Connection.IsInTransaction)
                throw new InvalidOperationException("Events and projection work require a shared transaction.");
        }

        public IList<IAggregateEvent> GetAllEvents(Guid rootId)
        {
            var events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId).OrderBy(x => x.Version).ToList();
            return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
        }

        public IList<IAggregateEvent> GetEventsAfterVersion(Guid rootId, int version)
        {
            var events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId && x.Version > version).OrderBy(x => x.Version).ToList();
            return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
        }

        public IList<IAggregateEvent> GetEventsUpToVersion(Guid rootId, int version)
        {
            var events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId && x.Version <= version).OrderBy(x => x.Version).ToList();
            return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
        }

        public int GetCurrentVersion(Guid rootId)
        {
            var result = this.Connection.Table<AggregateEvent>()
                .Where(x => x.AggregateId == rootId)
                    .OrderByDescending(x => x.Version)
                    .Take(1).ToList();

            return result.Select(x => x.Version).FirstOrDefault();
        }
    }
}