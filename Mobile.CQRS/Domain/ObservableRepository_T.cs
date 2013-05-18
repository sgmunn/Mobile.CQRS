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
    using Mobile.CQRS.Data;
    using Mobile.CQRS.Reactive;

    public class ObservableRepository<T> : IRepository<T>, IObservableRepository 
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

        public T GetById(Guid id)
        {
            return this.Repository.GetById(id);
        }

        public IList<T> GetAll()
        {
            return this.Repository.GetAll();
        }

        public virtual SaveResult Save(T instance)
        {
            var saveResult = this.Repository.Save(instance);
            
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

        public virtual void Delete(T instance)
        {
            this.Repository.Delete(instance);
            
            var modelChange = NotificationExtensions.CreateModelNotification(instance.Identity, null, ModelChangeKind.Deleted);
            this.changes.OnNext(modelChange);
        }

        public virtual void DeleteId(Guid id)
        {
            this.Repository.DeleteId(id);
            
            var modelChange = NotificationExtensions.CreateModelNotification(id, null, ModelChangeKind.Deleted);
            this.changes.OnNext(modelChange);
        }
    }
}