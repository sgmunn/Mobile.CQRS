//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="UnitOfWorkEventBus.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An event bus that performs all publishing in a single unit of work
    /// </summary>
    public class UnitOfWorkEventBus : IDomainNotificationBus, IUnitOfWork
    {
        private readonly IDomainNotificationBus bus;

        private readonly List<IDomainNotification> events;

        private readonly Action<IDomainNotification> onPublish;
        
        private readonly Action onCommit;

        public UnitOfWorkEventBus(IDomainNotificationBus bus)
        {
            if (bus == null)
                throw new ArgumentNullException("bus");

            this.events = new List<IDomainNotification>();
            this.bus = bus;
        }
        
        public UnitOfWorkEventBus(IDomainNotificationBus bus, Action onCommit) : this(bus)
        {
            if (bus == null)
                throw new ArgumentNullException("bus");

            this.onCommit = onCommit;
        }
        
        public UnitOfWorkEventBus(IDomainNotificationBus bus, Action<IDomainNotification> onPublish, Action onCommit) : this(bus)
        {
            this.onPublish = onPublish;
            this.onCommit = onCommit;
        }

        protected IDomainNotificationBus Bus
        {
            get
            {
                return this.bus;
            }
        }

        protected List<IDomainNotification> Events
        {
            get
            {
                return this.events;
            }
        }

        public virtual void Publish(IDomainNotification evt)
        {
            if (this.onPublish != null)
            {
                this.onPublish(evt);
            }

            this.Events.Add(evt);
        }

        public virtual IDisposable Subscribe(IObserver<IDomainNotification> subscriber)
        {
            throw new NotSupportedException();
        }

        public virtual void Dispose()
        {
            this.Events.Clear();
        }

        public virtual void Commit()
        {
            foreach (var evt in this.Events)
            {
                this.bus.Publish(evt);
            }

            this.Events.Clear();

            if (this.onCommit != null)
            {
                this.onCommit();
            }
        }
    }
}