// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DictionaryRepositoryBase_T.cs" company="sgmunn">
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
    using System.Threading;
    using System.Threading.Tasks;

    public abstract class DictionaryRepositoryBase<T> : IRepository<T>
    {
        private readonly Dictionary<Guid, T> storage;

        protected readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1);

        protected DictionaryRepositoryBase()
        {
            this.storage = new Dictionary<Guid, T>();
        }

        protected Dictionary<Guid, T> Storage
        {
            get
            {
                return this.storage;
            }
        }

        public T New()
        {
            return this.InternalNew();
        }

        public async Task<T> GetByIdAsync(Guid id)
        {
            await this.SyncLock.WaitAsync();
            try
            {
                if (this.storage.ContainsKey(id))
                {
                    return this.Storage[id];
                }

                return default(T);
            }
            finally
            {
                this.SyncLock.Release();
            }
        }

        public async Task<IList<T>> GetAllAsync()
        {
            await this.SyncLock.WaitAsync();
            try
            {
                return this.Storage.Values.ToList();
            }
            finally
            {
                this.SyncLock.Release();
            }
        }

        public async Task<SaveResult> SaveAsync(T instance)
        {
            await this.SyncLock.WaitAsync();
            try
            {
                return await this.InternalSaveAsync(instance);
            }
            finally
            {
                this.SyncLock.Release();
            }
        }

        public async Task DeleteAsync(T instance)
        {
            await this.SyncLock.WaitAsync();
            try
            {
                await this.InternalDeleteAsync(instance);
            }
            finally
            {
                this.SyncLock.Release();
            }
        }

        public async Task DeleteIdAsync(Guid id)
        {
            await this.SyncLock.WaitAsync();
            try
            {
                if (this.Storage.ContainsKey(id))
                {
                    this.Storage.Remove(id);
                }
            }
            finally
            {
                this.SyncLock.Release();
            }
        }

        public void Dispose()
        {
        }

        protected abstract T InternalNew();

        protected abstract Task<SaveResult> InternalSaveAsync(T instance);

        protected abstract Task InternalDeleteAsync(T instance);
    }
}