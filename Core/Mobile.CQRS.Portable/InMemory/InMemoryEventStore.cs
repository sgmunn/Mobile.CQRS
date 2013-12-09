// --------------------------------------------------------------------------------------------------------------------
// <copyright file="InMemoryEventStore.cs" company="sgmunn">
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
    using System.Threading.Tasks;

    public sealed class InMemoryEventStore : IEventStore
    {
        private readonly Dictionary<Guid, List<IAggregateEvent>> storage;

        public InMemoryEventStore()
        {
            this.storage = new Dictionary<Guid, List<IAggregateEvent>>();    
        }

        public void Dispose()
        {
        }

        public void SaveEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion)
        {
            lock(this.storage)
            {
                var currentVersion = this.GetCurrentVersion(aggregateId);
                if (currentVersion != expectedVersion)
                {
                    throw new ConcurrencyException(aggregateId, expectedVersion, currentVersion);
                }

                var savedEvents = this.GetEvents(aggregateId);
                savedEvents.AddRange(events);
                this.storage[aggregateId] = savedEvents;
            }
        }

        public void MergeEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion, int afterVersion)
        {
            lock(this.storage)
            {
                var currentVersion = this.GetCurrentVersion(aggregateId);
                if (currentVersion != expectedVersion)
                {
                    throw new ConcurrencyException(aggregateId, expectedVersion, currentVersion);
                }

                var savedEvents = this.GetEvents(aggregateId);
                // TODO: Test this
                // list is zero based
                // 0 1 2 3 4 5 6 7 8
                // 1 2 3 4 5 6 7 8 9
                savedEvents.RemoveRange(afterVersion, currentVersion - afterVersion);

                savedEvents.AddRange(events);
                this.storage[aggregateId] = savedEvents;
            }
        }
        
        public int GetCurrentVersion(Guid rootId)
        {
            List<IAggregateEvent> savedEvents;
            lock(this.storage)
            {
                savedEvents = this.GetEvents(rootId);
            }

            if (savedEvents.Count == 0)
            {
                return 0;
            }

            return savedEvents[savedEvents.Count - 1].Version;
        }

        public Task<IList<IAggregateEvent>> GetAllEventsAsync(Guid rootId)
        {
            List<IAggregateEvent> savedEvents;
            lock(this.storage)
            {
                savedEvents = this.GetEvents(rootId);
            }

            return Task.FromResult<IList<IAggregateEvent>>(savedEvents);
        }

        public Task<IList<IAggregateEvent>> GetEventsAfterVersionAsync(Guid rootId, int version)
        {
            List<IAggregateEvent> savedEvents;
            lock(this.storage)
            {
                savedEvents = this.GetEvents(rootId);
            }

            return Task.FromResult<IList<IAggregateEvent>>(savedEvents.Where(x => x.Version > version).ToList());
        }
        
        public Task<IList<IAggregateEvent>> GetEventsUpToVersionAsync(Guid rootId, int version)
        {
            List<IAggregateEvent> savedEvents;
            lock(this.storage)
            {
                savedEvents = this.GetEvents(rootId);
            }

            return Task.FromResult<IList<IAggregateEvent>>(savedEvents.Where(x => x.Version <= version).ToList());
        }

        private List<IAggregateEvent> GetEvents(Guid rootId)
        {
            List<IAggregateEvent> result;
            if (this.storage.TryGetValue(rootId, out result))
            {
                // return a new copy
                return result.ToList();
            }

            return new List<IAggregateEvent>();
        }
    }
}