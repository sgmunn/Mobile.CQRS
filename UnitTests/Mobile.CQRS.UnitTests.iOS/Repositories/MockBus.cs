
using System;
using System.Collections.Generic;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class MockBus : IDomainNotificationBus
    {
        public MockBus()
        {
            this.PublishedEvents = new List<IDomainNotification>();
        }

        public List<IDomainNotification> PublishedEvents { get; private set; }

        public void Publish(IDomainNotification evt)
        {
            this.PublishedEvents.Add(evt);
        }   

        public IDisposable Subscribe(IObserver<IDomainNotification> observer)
        {
            return null;
        }       
    }
}
