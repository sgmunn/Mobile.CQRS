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
        private readonly bool requiresTransaction;

        public EventStore(SQLiteConnection connection, ISerializer<IAggregateEvent> serializer)
            : this(connection, serializer, null) { }

        public EventStore(SQLiteConnection connection, ISerializer<IAggregateEvent> serializer, IReadModelQueueProducer outbox)
            : this(connection, serializer, outbox, false) { }

        public EventStore(SQLiteConnection connection, ISerializer<IAggregateEvent> serializer, IReadModelQueueProducer outbox,
            bool requiresTransaction) : base(connection)
        {
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            this.serializer = serializer;
            this.outbox = outbox;
            // A scope-bound store must never become an independently committing
            // store after a failed append rolls back its owner's transaction.
            this.requiresTransaction = requiresTransaction || outbox != null || connection.IsInTransaction;
            this.index = new AggregateIndexRepository(connection);

            connection.CreateTable<AggregateEvent>();
            connection.Execute("create unique index if not exists AggregateEvent_StreamVersion on AggregateEvent (AggregateId, Version)");
            connection.CreateTable<AggregateSnapshot>();
        }
        
        public bool EnqueuesReadModels => this.outbox != null;

        public void SaveEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion)
        {
            lock (this.Connection)
            {
                this.RequireTransaction();
                using var owned = this.Connection.IsInTransaction ? null : new SqlUnitOfWorkScope(this.Connection);
                this.Connection.RunInTransaction(() => {
                    var existing = this.GetAllEvents(aggregateId);
                    var type = existing.FirstOrDefault()?.AggregateType ?? events?.FirstOrDefault()?.AggregateType;
                    if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
                    if (existing.Count != expectedVersion) throw new ConcurrencyException(aggregateId, expectedVersion, existing.Count);
                    EventIntegrity.ValidateBatch(aggregateId, type, events, expectedVersion);
                    var rows = this.Serialize(aggregateId, events);
                    if (rows.Any(row => this.Connection.Find<AggregateEvent>(row.Identity) != null))
                        throw new InvalidOperationException("Event identity already exists.");
                    if (rows.Count == 0) return;
                    this.index.UpdateIndex(aggregateId, expectedVersion, rows.Last().Version);
                    foreach (var row in rows) this.Connection.Insert(row);
                    this.outbox?.Enqueue(aggregateId, type, rows[0].Version);
                });
                owned?.Commit();
            }
        }

        public void MergeEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion, int fromVersion)
        {
            if (!this.Connection.IsInTransaction)
                throw new InvalidOperationException("History replacement and snapshot invalidation require a transaction.");
            this.Connection.RunInTransaction(() => {
                var existing = this.GetAllEvents(aggregateId);
                if (existing.Count != expectedVersion) throw new ConcurrencyException(aggregateId, expectedVersion, existing.Count);
                if (fromVersion < 0 || fromVersion > existing.Count) throw new ArgumentOutOfRangeException(nameof(fromVersion));
                var type = existing.FirstOrDefault()?.AggregateType ?? events?.FirstOrDefault()?.AggregateType;
                EventIntegrity.ValidateBatch(aggregateId, type, events, fromVersion);
                var rows = this.Serialize(aggregateId, events);
                foreach (var row in rows)
                {
                    var collision = this.Connection.Find<AggregateEvent>(row.Identity);
                    if (collision != null && (collision.AggregateId != aggregateId || collision.Version <= fromVersion))
                        throw new InvalidOperationException("Event identity already exists in retained history.");
                }
                var version = checked(fromVersion + rows.Count);
                if (version == 0) this.Connection.Execute("delete from AggregateIndex where Identity = ?", aggregateId);
                else this.index.UpdateIndex(aggregateId, expectedVersion, version);
                this.Connection.Execute("delete from AggregateEvent where AggregateId = ? and Version > ?", aggregateId, fromVersion);
                foreach (var row in rows) this.Connection.Insert(row);
                this.Connection.Execute("delete from AggregateSnapshot where Identity = ?", aggregateId);
                if (type != null) this.outbox?.Enqueue(aggregateId, type, 0);
            });
        }

        private List<AggregateEvent> Serialize(Guid id, IList<IAggregateEvent> events)
        {
            var rows = events.Select(evt => new AggregateEvent {
                AggregateId = id, Identity = evt.Identity, Version = evt.Version,
                AggregateType = evt.AggregateType, EventData = this.serializer.SerializeToString(evt),
            }).ToList();
            foreach (var row in rows) this.Deserialize(row);
            return rows;
        }

        private IAggregateEvent Deserialize(AggregateEvent row)
        {
            var evt = this.serializer.DeserializeFromString(row.EventData);
            if (evt == null || evt.Identity != row.Identity || evt.AggregateId != row.AggregateId
                || evt.AggregateType != row.AggregateType || evt.Version != row.Version)
                throw new InvalidOperationException("Serialized event metadata does not match its envelope.");
            return evt;
        }

        private void RequireTransaction()
        {
            if (this.requiresTransaction && !this.Connection.IsInTransaction)
                throw new InvalidOperationException("This event store requires its owner's active transaction; discard a failed scope.");
        }

        public IList<IAggregateEvent> GetAllEvents(Guid id)
        {
            var rows = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == id).OrderBy(x => x.Version).ToList();
            var events = rows.Select(this.Deserialize).ToList();
            if (events.Count != 0) EventIntegrity.ValidateBatch(id, events[0].AggregateType, events, 0);
            return events;
        }

        public IList<IAggregateEvent> GetEventsAfterVersion(Guid id, int version)
            => this.GetAllEvents(id).Where(x => x.Version > version).ToList();
        public IList<IAggregateEvent> GetEventsUpToVersion(Guid id, int version)
            => this.GetAllEvents(id).Where(x => x.Version <= version).ToList();
        public int GetCurrentVersion(Guid id) => this.GetAllEvents(id).Count;
    }
}
