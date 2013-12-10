// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ObservableRepository_T.cs" company="sgmunn">
//   (c) sgmunn 2013  
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
    using Mobile.CQRS.Reactive;
    using System.Threading.Tasks;

    public class ObservableRepository<T> : IRepository<T>, IObservableRepository, IScopedRepository 
        where T : IUniqueId
    {
        private readonly IRepository<T> repository;
        
        private readonly Subject<IDomainNotification> changes;

        public ObservableRepository(IRepository<T> repository)
        {
            this.repository = repository;
            this.changes = new Subject<IDomainNotification>();
        }

        protected IRepository<T> Repository
        {
            get
            {
                return this.repository;
            }
        }
        
        public IObservable<IDomainNotification> Changes
        {
            get
            {
                return this.changes;
            }
        }

        public void Dispose()
        {
            this.Repository.Dispose();
        }

        public T New()
        {
            return this.Repository.New();
        }

        public Task<T> GetByIdAsync(Guid id)
        {
            return this.Repository.GetByIdAsync(id);
        }

        public Task<IList<T>> GetAllAsync()
        {
            return this.Repository.GetAllAsync();
        }

        public async virtual Task<SaveResult> SaveAsync(T instance)
        {
            var saveResult = await this.Repository.SaveAsync(instance).ConfigureAwait(false);
            
            IDomainNotification modelChange = null; 
            switch (saveResult)
            {
                case SaveResult.Added:
                    modelChange = NotificationExtensions.CreateModelNotification(instance.Identity, instance, ModelChangeKind.Added);
                    break;
                case SaveResult.Updated:
                    modelChange = NotificationExtensions.CreateModelNotification(instance.Identity, instance, ModelChangeKind.Changed);
                    break;
            }

            if (modelChange != null)
            {
                this.changes.OnNext(modelChange);
            }

            return saveResult;
        }

        public async virtual Task DeleteAsync(T instance)
        {
            await this.Repository.DeleteAsync(instance).ConfigureAwait(false);
            
            var modelChange = NotificationExtensions.CreateModelNotification(instance.Identity, null, ModelChangeKind.Deleted);
            this.changes.OnNext(modelChange);
        }

        public async virtual Task DeleteIdAsync(Guid id)
        {
            await this.Repository.DeleteIdAsync(id).ConfigureAwait(false);
            
            var modelChange = NotificationExtensions.CreateModelNotification(id, null, ModelChangeKind.Deleted);
            this.changes.OnNext(modelChange);
        }

        public Task DeleteAllInScopeAsync(Guid scopeId)
        {
            var scoped = this.Repository as IScopedRepository;
            if (scoped == null)
            {
                throw new NotSupportedException();
            }

            return scoped.DeleteAllInScopeAsync(scopeId);
        }
    }
}