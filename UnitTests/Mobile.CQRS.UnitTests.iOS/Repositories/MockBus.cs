
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class MockBus : IDomainNotificationBus
    {
        public MockBus()
        {
            this.PublishedEvents = new List<IDomainNotification>();
        }

        public List<IDomainNotification> PublishedEvents { get; private set; }

        public Task PublishAsync(IDomainNotification evt)
        {
            this.PublishedEvents.Add(evt);
            return TaskHelpers.Empty;
        }   

        public IDisposable Subscribe(IObserver<IDomainNotification> observer)
        {
            return null;
        }       
    }
}
