// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UnitOfWorkRepository_T.cs" company="sgmunn">
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

namespace Mobile.CQRS
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public class UnitOfWorkRepository<T> : IUnitOfWorkRepository<T> 
        where T : IUniqueId
    {
        private readonly IRepository<T> repository;

        private readonly Dictionary<Guid, IUniqueId> savedItems;
        
        private readonly List<Guid> deletedItemKeys;

        private readonly List<Guid> loadedItemKeys;

        public UnitOfWorkRepository(IRepository<T> repository)
        {
            this.repository = repository;

            this.savedItems = new Dictionary<Guid, IUniqueId>();
            this.deletedItemKeys = new List<Guid>();
            this.loadedItemKeys = new List<Guid>();
        }

        protected IRepository<T> Repository
        {
            get
            {
                return this.repository;
            }
        }

        public void Dispose()
        {
            this.Repository.Dispose();
            this.savedItems.Clear();
            this.deletedItemKeys.Clear();
        }

        public T New()
        {
            return this.repository.New();
        }

        public async Task<T> GetByIdAsync(Guid id)
        {
            if (this.deletedItemKeys.Any(x => x == id))
            {
                return default(T);
            }

            if (this.savedItems.ContainsKey(id))
            {
                return (T)this.savedItems[id];
            }

            var item = await this.repository.GetByIdAsync(id).ConfigureAwait(false);

            if (item != null)
            {
                this.loadedItemKeys.Add(id);
            }

            return item;
        }

        public async Task<IList<T>> GetAllAsync()
        {
            var saved = this.savedItems.Values.Cast<T>();

            var allItems = await this.Repository.GetAllAsync().ConfigureAwait(false);

            return saved.Union(allItems).Where(x => !this.deletedItemKeys.Any(y => y == x.Identity)).ToList();
        }

        public async virtual Task<SaveResult> SaveAsync(T instance)
        {
            if (this.savedItems.ContainsKey(instance.Identity))
            {
                this.savedItems[instance.Identity] = instance;
                return SaveResult.Updated;
            }

            this.savedItems[instance.Identity] = instance;

            if (this.loadedItemKeys.Contains(instance.Identity))
            {
                return SaveResult.Updated;
            }
            else
            {
                // we don't know if the item exists or not, so for now just get the item
                var realItem = await this.repository.GetByIdAsync(instance.Identity).ConfigureAwait(false);
                if (realItem != null)
                {
                    this.loadedItemKeys.Add(instance.Identity);
                    return SaveResult.Updated;
                }
            }

            return SaveResult.Added;
        }

        public virtual Task DeleteAsync(T instance)
        {
            var id = instance.Identity;
            return this.DeleteIdAsync(id);
        }

        public virtual Task DeleteIdAsync(Guid id)
        {
            if (!this.deletedItemKeys.Any(x => x == id))
            {
                this.deletedItemKeys.Add(id);
            }

            return TaskHelpers.Empty;
        }

        public async virtual Task CommitAsync()
        {
            // TODO: can we await each in parallel
            foreach (var item in this.savedItems.Values.Cast<T>())
            {
                await this.Repository.SaveAsync(item).ConfigureAwait(false);
            }

            foreach (var id in this.deletedItemKeys)
            {
                await this.Repository.DeleteIdAsync(id).ConfigureAwait(false);
            }
        }
    }
}