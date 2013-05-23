// --------------------------------------------------------------------------------------------------------------------
// <copyright file="InMemoryEventStore_T.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mobile.CQRS.Data;
    
    public class InMemoryEventStore<T> : DictionaryRepositoryBase<IAggregateEvent>, IEventStore 
        where T : IAggregateEvent, new() 
    {
        public IList<IAggregateEvent> GetAllEvents(Guid rootId)
        {
            return this.GetAll().Where(x => x.AggregateId == rootId).OrderBy(x => x.Version).ToList();
        }

        public IList<IAggregateEvent> GetEventsAfterVersion(Guid rootId, int version)
        {
            return this.GetAll().Where(x => x.AggregateId == rootId && x.Version > version).OrderBy(x => x.Version).ToList();
        }

        public int GetCurrentVersion(Guid rootId)
        {
            var allEvents = this.GetAllEvents(rootId);

            if (allEvents.Any())
            {
                return allEvents.Last().Version;
            }

            return 0;
        }

        public IList<IAggregateEvent> GetEventsAfterEvent(Guid eventId)
        {
            var result = new List<IAggregateEvent>();
            var allEvents = this.GetAll();

            if (eventId == Guid.Empty)
            {
                return allEvents;
            }

            bool found = false;
            foreach (var evt in allEvents)
            {
                if (found)
                {
                    result.Add(evt);
                }

                if (evt.Identity == eventId)
                {
                    found = true;
                }
            }

            return result;
        }

        public void SaveEvents(Guid rootId, IList<IAggregateEvent> events)
        {
            foreach (var evt in events)
            {
                this.InternalSave(evt);
            }
        }

        protected override IAggregateEvent InternalNew()
        {
            return new T(); 
        }

        protected override SaveResult InternalSave(IAggregateEvent evt)
        {
            if (this.Storage.ContainsKey(evt.Identity))
            {
                this.Storage[evt.Identity] = evt;
                return SaveResult.Updated;
            }

            this.Storage[evt.Identity] = evt;
            return SaveResult.Added;
        }

        protected override void InternalDelete(IAggregateEvent evt)
        {
            if (this.Storage.ContainsKey(evt.Identity))
            {
                this.Storage.Remove(evt.Identity);
            }
        }
    }
}