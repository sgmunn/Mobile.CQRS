// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AggregateRepository_T.cs" company="sgmunn">
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
    using Mobile.CQRS.Reactive;

    public class AggregateRepository<T> : IAggregateRepository<T>, IObservableRepository where T : IAggregateRoot, new()
    {
        private readonly IEventStore eventStore;

        private readonly IAggregateManifestRepository manifest;

        private readonly ISnapshotRepository snapshotRepository;

        private readonly Subject<IDomainNotification> changes;

        public AggregateRepository(IAggregateManifestRepository manifest,
                                   IEventStore eventStore, 
                                   ISnapshotRepository snapshotRepository)
        {
            // we have to have either an eventStore or a snapshotRepository
            if (manifest == null)
                throw new ArgumentNullException("manifest");
            if (eventStore == null && snapshotRepository == null)
                throw new InvalidOperationException("An EventStore or Snapshot Repository is required");

            this.changes = new Subject<IDomainNotification>();
            this.manifest = manifest;
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

        public T New()
        {
            return new T();
        }

        public T GetById(Guid id)
        {
            var snapshot = this.GetSnapshot(id);
            var version = snapshot != null ? snapshot.Version : 0;
            var eventsAfterSnapshot = this.GetEventsAfterVersion(id, version);

            if (version == 0 && eventsAfterSnapshot.Count == 0)
            {
                return default(T);
            }

            var result = this.New();

            result.Identity = id;
            if (!this.LoadFromFromSnapshot(result, snapshot))
            {
                // we failed to load
                if (snapshot != null)
                {
                    // a little bit inefficient, but we have to go back and get all the events
                    eventsAfterSnapshot = this.eventStore.GetAllEvents(id);
                }
            }

            ((IEventSourced)result).LoadFromEvents(eventsAfterSnapshot);

            return result;
        }

        public IList<T> GetAll()
        {
            throw new NotSupportedException();
        }

        public SaveResult Save(T instance)
        {
            if (!instance.UncommittedEvents.Any())
            {
                return SaveResult.None;
            }

            var expectedVersion = instance.UncommittedEvents.First().Version - 1;
            this.EnsureConcurrency(instance.Identity, expectedVersion);
            var newVersion = instance.UncommittedEvents.Last().Version;

            // update the manifest without reading it - 
            this.manifest.UpdateManifest(instance.AggregateType, instance.Identity, expectedVersion, newVersion);

            var snapshotChange = this.SaveSnapshot(instance);
            this.SaveEvents(instance);

            this.PublishEvents(instance.UncommittedEvents, snapshotChange);
            instance.Commit();

            return expectedVersion == 0 ? SaveResult.Added : SaveResult.Updated;
        }

        public void Delete(T instance)
        {
            throw new NotSupportedException();
        }

        public void DeleteId(Guid id)
        {
            throw new NotSupportedException();
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
        
        private void PublishEvents(IEnumerable<IAggregateEvent> events, IDomainNotification snapshotChange)
        {
            foreach (var evt in events.ToList())
            {
                var modelChange = NotificationExtensions.CreateNotification(typeof(T), evt);
                this.changes.OnNext(modelChange);
            }

            if (snapshotChange != null)
            {
                this.changes.OnNext(snapshotChange);
            }
        }

        private void SaveEvents(T instance)
        {
            if (this.eventStore != null)
            {
                this.eventStore.SaveEvents(instance.Identity, instance.UncommittedEvents.ToList());
            }
        }

        private IDomainNotification SaveSnapshot(T instance)
        {
            if (this.snapshotRepository != null && this.ShouldSaveSnapshot(instance))
            {
                var snapshot = ((ISnapshotSupport)instance).GetSnapshot();
                var saveResult = this.snapshotRepository.Save(snapshot);

                return NotificationExtensions.CreateModelNotification(instance.Identity, snapshot, saveResult == SaveResult.Added ? ModelChangeKind.Added : ModelChangeKind.Changed);
            }

            return null;
        }

        private bool ShouldSaveSnapshot(T instance)
        {
            if (this.eventStore == null)
            {
                return true;
            }

            return (instance is ISnapshotSupport) && ((ISnapshotSupport)instance).ShouldSaveSnapshot();
        }

        private int EnsureConcurrency(Guid id, int expectedVersion)
        {
            int currentVersion = 0;

            // the eventStore is the truth!
            if (this.eventStore != null)
            {
                currentVersion = this.eventStore.GetCurrentVersion(id);
            }
            else
            {
                var current = this.snapshotRepository.GetById(id);
                if (current != null)
                {
                    currentVersion = current.Version;
                }
            }
            
            if ((currentVersion == 0 && expectedVersion != 0) || (currentVersion != expectedVersion))
            {
                throw new ConcurrencyException(id, expectedVersion, currentVersion);
            }

            return currentVersion;
        }

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

        private IList<IAggregateEvent> GetEventsAfterVersion(Guid id, int version)
        {
            if (this.eventStore != null)
            {
                return this.eventStore.GetEventsAfterVersion(id, version);
            }

            return new List<IAggregateEvent>();
        }

        private ISnapshot GetSnapshot(Guid id)
        {
            if (this.snapshotRepository != null)
            {
                return this.snapshotRepository.GetById(id);
            }

            return null;
        }
    }
}