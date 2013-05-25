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

namespace Mobile.CQRS.Domain.SQLite
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mobile.CQRS.Data.SQLite;
    using Mobile.CQRS.Serialization;

    public class EventStore : SqlRepository<AggregateEvent>, IEventStore
    {
        private readonly ISerializer<IAggregateEvent> serializer;

        public EventStore(SQLiteConnection connection, ISerializer<IAggregateEvent> serializer) : base(connection)
        {
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            this.serializer = serializer;
        }

        public IList<IAggregateEvent> GetAllEvents(Guid rootId)
        {
            IList<AggregateEvent> events;

            lock (this.Connection)
            {
                events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId).OrderBy(x => x.Version).ToList();
            }

            return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
        }

        public IList<IAggregateEvent> GetEventsAfterVersion(Guid rootId, int version)
        {
            IList<AggregateEvent> events;
            lock (this.Connection)
            {
                events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId && x.Version > version).OrderBy(x => x.Version).ToList();
            }
            
            return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
        }

        public IList<IAggregateEvent> GetEventsUpToVersion(Guid rootId, int version)
        {
            IList<AggregateEvent> events;
            lock (this.Connection)
            {
                events = this.Connection.Table<AggregateEvent>().Where(x => x.AggregateId == rootId && x.Version <= version).OrderBy(x => x.Version).ToList();
            }

            return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
        }

        public int GetCurrentVersion(Guid rootId)
        {
            lock (this.Connection)
            {
                var result = this.Connection.Table<AggregateEvent>()
                    .Where(x => x.AggregateId == rootId)
                        .OrderByDescending(x => x.Version)
                        .Take(1).ToList();

                return result.Select(x => x.Version).FirstOrDefault();
            }
        }

        public IList<IAggregateEvent> GetEventsAfterEvent(Guid eventId)
        {
            IList<AggregateEvent> events = null;
            lock (this.Connection)
            {
                if (eventId == Guid.Empty)
                {
                    events = this.Connection.Table<AggregateEvent>().OrderBy(x => x.GlobalKey).ToList();
                }
                else
                {
                    var evt = this.Connection.Table<AggregateEvent>().Where(x => x.Identity == eventId).FirstOrDefault();
                    if (evt != null)
                    {
                        events = this.Connection.Table<AggregateEvent>().Where(x => x.GlobalKey > evt.GlobalKey).OrderBy(x => x.GlobalKey).ToList();
                    }
                }
            }

            if (events != null)
            {
                return events.Select(evt => this.serializer.DeserializeFromString(evt.EventData)).ToList();
            }

            return new List<IAggregateEvent>();
        }

        public void SaveEvents(Guid rootId, IList<IAggregateEvent> events)
        {
            var serializedEvents = events.Select(evt => new AggregateEvent{
                AggregateId = rootId, 
                Identity = evt.Identity,
                Version = evt.Version,
                CommandId = evt.CommandId,
                EventData = this.serializer.SerializeToString(evt),
            }).ToList();

            lock (this.Connection)
            {
                foreach (var evt in serializedEvents)
                {
                    this.Connection.Insert(evt);
                }
            }
        }

        public void MergeEvents(Guid rootId, IList<IAggregateEvent> events, int fromVersion)
        {
            var serializedEvents = events.Select(evt => new AggregateEvent{
                AggregateId = rootId, 
                Identity = evt.Identity,
                Version = evt.Version,
                CommandId = evt.CommandId,
                EventData = this.serializer.SerializeToString(evt),
            }).ToList();
            
            lock (this.Connection)
            {
                this.Connection.Execute(string.Format("delete from AggregateEvent where AggregateId = ? and Version > {0}", fromVersion), rootId);

                foreach (var evt in serializedEvents)
                {
                    this.Connection.Insert(evt);
                }
            }
        }
    }
}