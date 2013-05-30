// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AggregateRegistration.cs" company="sgmunn">
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
    using System.Linq;

    public sealed class AggregateRegistration : IAggregateRegistration
    {
        private readonly Type aggregateType;
        
        private readonly Func<IAggregateRoot> instantiator;

        private readonly List<Func<IUnitOfWorkScope, IReadModelBuilder>> immediateBuilders;
        
        private readonly List<Func<IUnitOfWorkScope, IReadModelBuilder>> delayedBuilders;

        private AggregateRegistration(Type aggregateType, Func<IAggregateRoot> instantiator)
        {
            this.aggregateType = aggregateType;
            this.instantiator = instantiator;
            this.immediateBuilders = new List<Func<IUnitOfWorkScope, IReadModelBuilder>>();
            this.delayedBuilders = new List<Func<IUnitOfWorkScope, IReadModelBuilder>>();
        }

        private AggregateRegistration(AggregateRegistration registration)
        {
            this.aggregateType = registration.aggregateType;
            this.instantiator = registration.instantiator;
            this.immediateBuilders = new List<Func<IUnitOfWorkScope, IReadModelBuilder>>(registration.immediateBuilders);
            this.delayedBuilders = new List<Func<IUnitOfWorkScope, IReadModelBuilder>>(registration.delayedBuilders);
        }

        public Type AggregateType 
        { 
            get
            {
                return this.aggregateType;
            }
        }
        
        public static AggregateRegistration ForType<T>() where T : class, IAggregateRoot, new()
        {
            return new AggregateRegistration(typeof(T), () => new T());
        }

        public IAggregateRoot New()
        {
            return this.instantiator();
        }

        public IList<IReadModelBuilder> ImmediateReadModels(IUnitOfWorkScope scope)
        {
            return this.immediateBuilders.Select(r => r(scope)).ToList();
        }
        
        public IList<IReadModelBuilder> DelayedReadModels(IUnitOfWorkScope scope)
        {
            return this.delayedBuilders.Select(r => r(scope)).ToList();
        }

        public AggregateRegistration WithImmediateReadModel(Func<IUnitOfWorkScope, IReadModelBuilder> builderFactory)
        {
            var registration = new AggregateRegistration(this);
            registration.immediateBuilders.Add(builderFactory);
            return registration;
        }
        
        public AggregateRegistration WithDelayedReadModel(Func<IUnitOfWorkScope, IReadModelBuilder> builderFactory)
        {
            var registration = new AggregateRegistration(this);
            registration.delayedBuilders.Add(builderFactory);
            return registration;
        }
    }
}