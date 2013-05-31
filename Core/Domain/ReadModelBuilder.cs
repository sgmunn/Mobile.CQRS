// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ReadModelBuilder.cs" company="sgmunn">
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
    using Mobile.CQRS.Reactive;

    /// <summary>
    /// *
    /// * read model builders need to check the version of each event to make sure that they don't double process events
    /// *
    /// </summary>
    public abstract class ReadModelBuilder<T> : IReadModelBuilder
        where T : IUniqueId
    {
        private readonly IRepository<T> repository;

        private readonly List<IDomainNotification> updatedReadModels;

        protected ReadModelBuilder(IRepository<T> repository)
        {
            var observableRepo = new ObservableRepository<T>(repository);
            this.repository = observableRepo;
            this.updatedReadModels = new List<IDomainNotification>();

            observableRepo.Changes.Subscribe(evt => {
                this.updatedReadModels.Add(evt);
            });
        }

        public IRepository<T> Repository
        {
            get
            {
                return this.repository;
            }
        }

        public IEnumerable<IDomainNotification> Handle(IDomainNotification evt)
        {
            this.updatedReadModels.Clear();

            MethodExecutor.ExecuteMethod(this, evt.Event);

            return this.updatedReadModels;
        }

        public IEnumerable<IDomainNotification> Process(IEnumerable<IDomainNotification> events)
        {
            this.updatedReadModels.Clear();

            foreach (var evt in events)
            {
                MethodExecutor.ExecuteMethod(this, evt.Event);
            }

            return this.updatedReadModels;
        }
        
        public virtual void DeleteForAggregate(Guid aggregateId)
        {
            var scoped = this.Repository as IScopedRepository;
            if (scoped == null)
            {
                throw new NotSupportedException("Repository does not support IScopedRepository");
            }

            scoped.DeleteAllInScope(aggregateId);
        }
    }
}