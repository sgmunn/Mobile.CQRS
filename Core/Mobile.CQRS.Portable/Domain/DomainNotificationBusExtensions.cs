//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="DomainNotificationBusExtensions.cs" company="sgmunn">
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
    using System.Reflection;
    using Mobile.CQRS.Reactive;

    public static class DomainNotificationBusExtensions
    {
        /// <summary>
        /// Returns IDataModelEvents that are for the specified identity type.  These could be events or read model changes
        /// </summary>            
        public static IObservable<IDomainNotification> ForType<T>(this IObservable<IDomainNotification> source)
        {
            return source.Where(x => typeof(T).GetTypeInfo().IsAssignableFrom(x.Topic.ModelType.GetTypeInfo()));
        }

        /// <summary>
        /// Returns data model changes for the given identity, events or read models
        /// </summary>            
        public static IObservable<IDomainNotification> DataChangesForType<T>(this IObservable<IDomainNotification> source)
        {
            return source.ForType<T>()
                    .Where(x => typeof(IDomainNotification).GetTypeInfo().IsAssignableFrom(x.Event.GetType().GetTypeInfo()))
                    .Select(x => x.Event)
                    .Cast<IDomainNotification>();
        }

        /// <summary>
        /// Returns events for the given identity type
        /// </summary>            
        public static IObservable<IAggregateEvent> EventsForType<T>(this IObservable<IDomainNotification> source) 
            where T : IAggregateRoot
        {
            return source.ForType<T>()
                    .Where(x => typeof(IAggregateEvent).GetTypeInfo().IsAssignableFrom(x.Event.GetType().GetTypeInfo()))
                    .Select(x => x.Event)
                    .Cast<IAggregateEvent>();
        }
    }
}
