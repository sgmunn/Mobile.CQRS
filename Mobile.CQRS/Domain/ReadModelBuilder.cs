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
    using Mobile.CQRS.Data;

    public abstract class ReadModelBuilder<T> : IReadModelBuilder<T>, IObservableRepository
    {
        private readonly IRepository<T> repository;

        private readonly List<IModelNotification> updatedReadModels;

        private readonly IObservable<IModelNotification> changes;

        protected ReadModelBuilder(IRepository<T> repository)
        {
            this.repository = repository;
            this.updatedReadModels = new List<IModelNotification>();

            var observableRepo = repository as IObservableRepository;
            this.changes = observableRepo != null ? observableRepo.Changes : null;
        }

        public IRepository<T> Repository
        {
            get
            {
                return this.repository;
            }
        }

        public IEnumerable<IModelNotification> Handle(IModelNotification evt)
        {
            this.updatedReadModels.Clear();

            // now, for each domain event, call a method that takes the event and call it
            // todo: should we check for not handling the event or not.  read model builders probably don't need to 
            // handle *everthing*
            MethodExecutor.ExecuteMethod(this, evt.Event);

            return this.updatedReadModels;
        }

        public IObservable<IModelNotification> Changes
        {
            get
            {
                return this.changes;
            }
        }

        // TODO: read model builder
//        protected void NotifyReadModelChange(IDataModel readModel, bool deleted)
//        {
//            this.updatedReadModels.Add(new DataModelChange(readModel, deleted));
//        }
//
//        protected void NotifyReadModelChange(Guid id, bool deleted)
//        {
//            this.updatedReadModels.Add(new DataModelChange(id, deleted));
//        }
    }
}

