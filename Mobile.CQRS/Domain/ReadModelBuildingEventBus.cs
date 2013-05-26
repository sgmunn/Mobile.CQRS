// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ReadModelBuildingEventBus.cs" company="sgmunn">
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

    public class ReadModelBuildingEventBus : UnitOfWorkEventBus 
    {
        private readonly IList<IReadModelBuilder> builders;

        public ReadModelBuildingEventBus(IList<IReadModelBuilder> builders, IDomainNotificationBus bus)
            : base(bus)
        {
            if (builders == null)
                throw new ArgumentNullException("builders");

            this.builders = builders;
        }

        public override void Commit()
        {
            this.Events.AddRange(this.BuildReadModels());
            base.Commit();
        }

        protected virtual IList<IDomainNotification> BuildReadModels()
        {
            var result = new List<IDomainNotification>();

            var updatedReadModels = new List<IDomainNotification>();

            foreach (var evt in this.Events)
            {
                foreach (var builder in this.builders)
                {
                    updatedReadModels.AddRange(builder.Handle(evt));
                }
            }

            foreach (var readModel in updatedReadModels)
            {
                result.Add(readModel);
            }

            return result;
        }
    }
}