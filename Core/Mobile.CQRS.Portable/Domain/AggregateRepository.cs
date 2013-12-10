// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AggregateRepository.cs" company="sgmunn">
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
    using Mobile.CQRS.Reactive;

    public sealed class AggregateRepository : IAggregateRepository, IObservableRepository, IDisposable
    {
        private readonly Func<IAggregateRoot> factory;

        private readonly IEventStore eventStore;

        private readonly ISnapshotRepository snapshotRepository;

        private readonly Subject<IDomainNotification> changes;
        
        public AggregateRepository(Func<IAggregateRoot> factory, IEventStore eventStore)
        {
            if (factory == null)
                throw new ArgumentNullException("factory");
            if (eventStore == null)
                throw new ArgumentNullException("eventStore");

            this.changes = new Subject<IDomainNotification>();
            this.factory = factory;
            this.eventStore = eventStore;
        }
        
        public AggregateRepository(Func<IAggregateRoot> factory, ISnapshotRepository snapshotRepository)
        {
            if (factory == null)
                throw new ArgumentNullException("factory");
            if (snapshotRepository == null)
                throw new ArgumentNullException("snapshotRepository");

            this.changes = new Subject<IDomainNotification>();
            this.factory = factory;
            this.snapshotRepository = snapshotRepository;
        }

        public AggregateRepository(Func<IAggregateRoot> factory, IEventStore eventStore, ISnapshotRepository snapshotRepository)
        {
            if (factory == null)
                throw new ArgumentNullException("factory");
            if (eventStore == null && snapshotRepository == null)
                throw new InvalidOperationException("eventStore or snapshotRepository is required");

            this.changes = new Subject<IDomainNotification>();
            this.factory = factory;
            this.eventStore = eventStore;
            this.snapshotRepository = snapshotRepository;
        }
        
        public IObservable<IDomainNotification> Changes
        {
            get
            {
                return this.changes;
            }
        }

        public IAggregateRoot New()
        {
            return this.factory();
        }

        public async Task<IAggregateRoot> GetByIdAsync(Guid id)
        {
            var snapshot = await this.GetSnapshotAsync(id).ConfigureAwait(false);
            var version = snapshot != null ? snapshot.Version : 0;
            var eventsAfterSnapshot = await this.GetEventsAfterVersionAsync(id, version).ConfigureAwait(false);

            if (version == 0 && eventsAfterSnapshot.Count == 0)
            {
                return null;
            }

            var result = this.New();

            result.Identity = id;
            if (!this.LoadFromFromSnapshot(result, snapshot))
            {
                // we failed to load
                if (snapshot != null)
                {
                    // a little bit inefficient, but we have to go back and get all the events
                    eventsAfterSnapshot = await this.eventStore.GetAllEventsAsync(id).ConfigureAwait(false);
                }
            }

            ((IEventSourced)result).LoadFromEvents(eventsAfterSnapshot);

            return result;
        }

        public async Task<SaveResult> SaveAsync(IAggregateRoot instance)
        {
            if (!instance.UncommittedEvents.Any())
            {
                return SaveResult.None;
            }

            var expectedVersion = instance.UncommittedEvents.First().Version - 1;

            ////this.EnsureConcurrency(instance.Identity, expectedVersion);
            var newVersion = instance.UncommittedEvents.Last().Version;

            var snapshotChange = await this.SaveSnapshotAsync(instance, expectedVersion).ConfigureAwait(false);
            await this.SaveEventsAsync(instance, expectedVersion).ConfigureAwait(false);

            this.PublishEvents(instance, snapshotChange);
            instance.Commit();

            return expectedVersion == 0 ? SaveResult.Added : SaveResult.Updated;
        }

        public void Dispose()
        {
            if (this.snapshotRepository != null)
            {
                this.snapshotRepository.Dispose();
            }

            if (this.eventStore != null)
            {
                this.eventStore.Dispose();
            }
        }
        
        private void PublishEvents(IAggregateRoot instance, IDomainNotification snapshotChange)
        {
            foreach (var evt in instance.UncommittedEvents.ToList())
            {
                var modelChange = NotificationExtensions.CreateNotification(instance.GetType(), evt);
                this.changes.OnNext(modelChange);
            }

            if (snapshotChange != null)
            {
                this.changes.OnNext(snapshotChange);
            }
        }

        private Task SaveEventsAsync(IAggregateRoot instance, int expectedVersion)
        {
            return this.eventStore.SaveEventsAsync(instance.Identity, instance.UncommittedEvents.ToList(), expectedVersion);
        }

        private async Task<IDomainNotification> SaveSnapshotAsync(IAggregateRoot instance, int expectedVersion)
        {
            if (this.snapshotRepository != null && this.ShouldSaveSnapshot(instance))
            {
                var snapshot = ((ISnapshotSupport)instance).GetSnapshot();

                // we have to pass expected version if we don't have an event store
                SaveResult saveResult;
                if (this.eventStore == null)
                {
                    saveResult = await this.snapshotRepository.SaveAsync(snapshot, expectedVersion).ConfigureAwait(false);
                }
                else
                {
                    saveResult = await this.snapshotRepository.SaveAsync(snapshot).ConfigureAwait(false);
                }

                return NotificationExtensions.CreateModelNotification(instance.Identity, snapshot, saveResult == SaveResult.Added ? ModelChangeKind.Added : ModelChangeKind.Changed);
            }

            return null;
        }

        private bool ShouldSaveSnapshot(IAggregateRoot instance)
        {
            if (this.eventStore == null)
            {
                return true;
            }

            return (instance is ISnapshotSupport) && ((ISnapshotSupport)instance).ShouldSaveSnapshot();
        }

////        private int EnsureConcurrency(Guid id, int expectedVersion)
////        {
////            int currentVersion = 0;
////
////            // the eventStore is the truth!
////            if (this.eventStore != null)
////            {
////                currentVersion = this.eventStore.GetCurrentVersion(id);
////            }
////            else
////            {
////                var current = this.snapshotRepository.GetById(id);
////                if (current != null)
////                {
////                    currentVersion = current.Version;
////                }
////            }
////            
////            if ((currentVersion == 0 && expectedVersion != 0) || (currentVersion != expectedVersion))
////            {
////                throw new ConcurrencyException(id, expectedVersion, currentVersion);
////            }
////
////            return currentVersion;
////        }

        private bool LoadFromFromSnapshot(IAggregateRoot aggregate, ISnapshot snapshot)
        {
            try
            {
                if (snapshot != null && aggregate != null)
                {
                    ((ISnapshotSupport)aggregate).LoadFromSnapshot(snapshot);
                    return true;
                }
            }
            catch (UnsupportedSnapshotException)
            {
                return false;
            }

            return false;
        }

        private Task<IList<IAggregateEvent>> GetEventsAfterVersionAsync(Guid id, int version)
        {
            if (this.eventStore != null)
            {
                return this.eventStore.GetEventsAfterVersionAsync(id, version);
            }

            return Task.FromResult<IList<IAggregateEvent>>(new List<IAggregateEvent>());
        }

        private Task<ISnapshot> GetSnapshotAsync(Guid id)
        {
            if (this.snapshotRepository != null)
            {
                return this.snapshotRepository.GetByIdAsync(id);
            }

            return Task.FromResult<ISnapshot>(null);
        }
    }
}