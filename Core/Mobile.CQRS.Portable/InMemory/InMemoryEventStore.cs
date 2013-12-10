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

namespace Mobile.CQRS.InMemory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Threading;
    using Mobile.CQRS.Domain;

    public sealed class InMemoryEventStore : IEventStore
    {
        private readonly Dictionary<Guid, List<IAggregateEvent>> storage;

        private readonly SemaphoreSlim syncLock = new SemaphoreSlim(1);

        public InMemoryEventStore()
        {
            this.storage = new Dictionary<Guid, List<IAggregateEvent>>();    
        }

        public void Dispose()
        {
        }

        public async Task SaveEventsAsync(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion)
        {
            await this.syncLock.WaitAsync();
            try
            {
                var currentVersion = await this.GetCurrentVersionAsync(aggregateId);
                if (currentVersion != expectedVersion)
                {
                    throw new ConcurrencyException(aggregateId, expectedVersion, currentVersion);
                }

                var savedEvents = this.GetEvents(aggregateId);
                savedEvents.AddRange(events);
                this.storage[aggregateId] = savedEvents;
            }
            finally
            {
                this.syncLock.Release();
            }
        }

        public async Task MergeEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion, int afterVersion)
        {
            await this.syncLock.WaitAsync();
            try
            {
                var currentVersion = await this.GetCurrentVersionAsync(aggregateId);
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
            finally
            {
                this.syncLock.Release();
            }
        }
        
        public async Task<int> GetCurrentVersionAsync(Guid rootId)
        {
            await this.syncLock.WaitAsync();
            try
            {
                var savedEvents = this.GetEvents(rootId);

                if (savedEvents.Count == 0)
                {
                    return 0;
                }

                return savedEvents[savedEvents.Count - 1].Version;
            }
            finally
            {
                this.syncLock.Release();
            }
        }

        public async Task<IList<IAggregateEvent>> GetAllEventsAsync(Guid rootId)
        {
            await this.syncLock.WaitAsync();
            try
            {
                var savedEvents = this.GetEvents(rootId);
                return savedEvents;
            }
            finally
            {
                this.syncLock.Release();
            }
        }

        public async Task<IList<IAggregateEvent>> GetEventsAfterVersionAsync(Guid rootId, int version)
        {
            await this.syncLock.WaitAsync();
            try
            {
                var savedEvents = this.GetEvents(rootId);
                return savedEvents.Where(x => x.Version > version).ToList();
            }
            finally
            {
                this.syncLock.Release();
            }
        }
        
        public async Task<IList<IAggregateEvent>> GetEventsUpToVersionAsync(Guid rootId, int version)
        {
            await this.syncLock.WaitAsync();
            try
            {
                var savedEvents = this.GetEvents(rootId);
                return savedEvents.Where(x => x.Version <= version).ToList();
            }
            finally
            {
                this.syncLock.Release();
            }
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