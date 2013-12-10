//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="InMemorySnapshotRepository_T.cs" company="sgmunn">
//    (c) sgmunn 2012  
//
//    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//    documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//    the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//    to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//    The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//    the Software.
//
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//    THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//    CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//    IN THE SOFTWARE.
//  </copyright>
//  --------------------------------------------------------------------------------------------------------------------

namespace Mobile.CQRS.InMemory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Mobile.CQRS.Domain;

    // TODO: fix up locking with async in in memory repository

    public class InMemorySnapshotRepository<T> : DictionaryRepositoryBase<T>, ISnapshotRepository
        where T : class, ISnapshot, new() 
    {
        public InMemorySnapshotRepository()
        {
        }

        public async Task<SaveResult> SaveAsync(ISnapshot instance, int expectedVersion)
        {
            lock (this.Storage)
            {
                // get the current version 
                var current = this.GetByIdAsync(instance.Identity).Result;
                if (current != null && current.Version != expectedVersion)
                {
                    throw new ConcurrencyException(instance.Identity, expectedVersion, current.Version);
                }

                return this.SaveAsync(instance).Result;
            }
        }

        protected override T InternalNew()
        {
            return new T(); 
        }

        protected override Task<SaveResult> InternalSaveAsync(T instance)
        {
            lock (this.Storage)
            {
                if (this.Storage.ContainsKey(instance.Identity))
                {
                    this.Storage[instance.Identity] = instance;
                    return Task.FromResult<SaveResult>(SaveResult.Updated);
                }

                this.Storage[instance.Identity] = instance;
                return Task.FromResult<SaveResult>(SaveResult.Added);
            }
        }

        protected override Task InternalDeleteAsync(T instance)
        {
            if (this.Storage.ContainsKey(instance.Identity))
            {
                this.Storage.Remove(instance.Identity);
            }

            return TaskHelpers.Empty;
        }

        public new ISnapshot New()
        {
            return this.InternalNew();
        }

        public async new Task<ISnapshot> GetByIdAsync(Guid id)
        {
            lock (this.Storage)
            {
                return base.GetByIdAsync(id).Result;
            }
        }

        public async new Task<IList<ISnapshot>> GetAllAsync()
        {
            lock (this.Storage)
            {
                return base.GetAllAsync().Result.Cast<ISnapshot>().ToList();
            }
        }

        public Task<SaveResult> SaveAsync(ISnapshot snapshot)
        {
            lock (this.Storage)
            {
                return this.InternalSaveAsync((T)snapshot);
            }
        }

        public Task DeleteAsync(ISnapshot snapshot)
        {
            lock (this.Storage)
            {
                return this.InternalDeleteAsync((T)snapshot);
            }
        }
    }
}