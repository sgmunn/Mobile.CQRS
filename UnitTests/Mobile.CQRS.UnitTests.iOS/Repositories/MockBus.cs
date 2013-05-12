
using System;
using System.Collections.Generic;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class MockBus : IModelNotificationBus
    {
        public MockBus()
        {
            this.PublishedEvents = new List<IModelNotification>();
        }

        public List<IModelNotification> PublishedEvents { get; private set; }

        public void Publish(IModelNotification evt)
        {
            this.PublishedEvents.Add(evt);
        }   

        public IDisposable Subscribe(IObserver<IModelNotification> observer)
        {
            return null;
        }       
    }
}
