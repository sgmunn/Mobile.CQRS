// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AggregateRootBase.cs" company="sgmunn">
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

    public abstract class AggregateRootBase : IAggregateRoot, IEventSourced
    {
        private readonly List<IAggregateEvent> uncommittedEvents;

        public static string GetAggregateTypeDescriptor<T>() 
        {
            return GetAggregateTypeDescriptor(typeof(T));
        }
        
        public static string GetAggregateTypeDescriptor(Type type) 
        {
            var attrs = type.GetCustomAttributes(typeof(AggregateTypeAttribute), false);
            if (attrs.Length == 1)
            {
                return ((AggregateTypeAttribute)attrs[0]).TypeDescriptor;
            }

            return type.FullName;
        }

        protected AggregateRootBase()
        {
            this.uncommittedEvents = new List<IAggregateEvent>();
        }

        public Guid Identity { get; set; }

        public int Version { get; protected set; }

        public IEnumerable<IAggregateEvent> UncommittedEvents
        {
            get
            {
                return this.uncommittedEvents;
            }
        }

        public void Commit()
        {
            this.uncommittedEvents.Clear();
        }
        
        public void LoadFromEvents(IList<IAggregateEvent> events)
        {
            this.ApplyEvents(events);
        }

        protected void ApplyEvents(IList<IAggregateEvent> events)
        {
            foreach (var evt in events)
            {
                this.ApplyEvent(evt);
                this.Version = evt.Version;
            }
        }

        protected void RaiseEvent(Guid commandId, IAggregateEvent evt)
        {
            if (commandId == Guid.Empty)
            {
                throw new ArgumentNullException("commandId", "Cannot raise an event without specifying the command id");
            }

            this.Version++;

            evt.Identity = Guid.NewGuid();
            evt.AggregateId = this.Identity;
            evt.AggregateType = GetAggregateTypeDescriptor(this.GetType());
            evt.Version = this.Version;
            evt.Timestamp = DateTime.UtcNow;
            evt.CommandId = commandId;

            this.ApplyEvent(evt);
            this.uncommittedEvents.Add(evt);
        }

        private void ApplyEvent(object evt)
        {
            if (!MethodExecutor.ExecuteMethod(this, evt))
            {
                throw new MissingMethodException(string.Format("Aggregate {0} does not support a method that can be called with {1}", this, evt));
            }
        }
    }
}