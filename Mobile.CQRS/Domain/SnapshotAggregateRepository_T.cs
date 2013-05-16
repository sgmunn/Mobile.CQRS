// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SnapshotAggregateRepository_T.cs" company="sgmunn">
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
     
    // todo: snapshot repository needs a way to serialize complex data members so that we can still use sqlite
    // at the moment this will not handle internal state with complicated objects
    // an alternative is for the aggregate to return a snapshot that is serialized -- ie different to its internal state

    public class SnapshotAggregateRepository<T> : IAggregateRepository<T>
        where T : IAggregateRoot, new()
    {
        private readonly ISnapshotRepository repository;
        
        private readonly IModelNotificationBus eventBus;

        private readonly IAggregateManifestRepository manifest;
  
        public SnapshotAggregateRepository(ISnapshotRepository repository, IAggregateManifestRepository manifest, IModelNotificationBus eventBus)
        {
            this.repository = repository;
            this.eventBus = eventBus;
            this.manifest = manifest;
        }

        public T New()
        {
            return new T();
        }

        public T GetById(Guid id)
        {
            var snapshot = this.repository.GetById(id);
            
            if (snapshot == null)
            {
                return default(T);
            }
            
            var result = this.New();
   
            ((ISnapshotSupport)result).LoadFromSnapshot(snapshot);

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

            var current = this.GetById(instance.Identity);

            int expectedVersion = instance.UncommittedEvents.First().Version - 1;

            if ((current == null && expectedVersion != 0) || (current != null && current.Version != expectedVersion))
            {
                throw new ConcurrencyException(instance.Identity, expectedVersion, current.Version);
            }
   
            var snapshot = ((ISnapshotSupport)instance).GetSnapshot() as ISnapshot;

            this.manifest.UpdateManifest(instance.Identity, expectedVersion, snapshot.Version);

            var saveResult = this.repository.Save(snapshot);

            IModelNotification modelChange = null; 
            switch (saveResult)
            {
                case SaveResult.Added:
                    modelChange = Data.Notifications.CreateModelNotification(instance.Identity, instance, ModelChangeKind.Added);
                    break;
                case SaveResult.Updated:
                    modelChange = Data.Notifications.CreateModelNotification(instance.Identity, instance, ModelChangeKind.Changed);
                    break;
            }

            this.PublishEvents(instance.UncommittedEvents, modelChange);
            instance.Commit();

            return expectedVersion == 0 ? SaveResult.Added : SaveResult.Updated;
        }

        public void Delete(T instance)
        {
            // TODO: should snapshot repo publish deletes ?
            this.repository.DeleteId(instance.Identity);
        }

        public void DeleteId(Guid id)
        {
            this.repository.DeleteId(id);
        }

        public void Dispose()
        {
            this.repository.Dispose();
        }

        private void PublishEvents(IEnumerable<IAggregateEvent> events, IModelNotification modelNotification)
        {
            if (this.eventBus != null)
            {
                foreach (var evt in events.ToList())
                {
                    var modelChange = Notifications.CreateNotification(typeof(T), evt);
                    this.eventBus.Publish(modelChange);
                }

                this.eventBus.Publish(modelNotification);
           }
        }
    }
}