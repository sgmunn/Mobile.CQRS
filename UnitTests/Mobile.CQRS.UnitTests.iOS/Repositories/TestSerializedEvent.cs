
using System;
using NUnit.Framework;

namespace Mobile.CQRS.Domain.UnitTests.Repositories
{
    public class TestSerializedEvent : ISerializedAggregateEvent
    {
        public TestSerializedEvent()
        {
            this.Identity = Guid.NewGuid();
        }

        public Guid Identity { get; set; }

        public Guid AggregateId { get; set; }

        public int Version { get; set; }

        public string EventData { get; set; }
    }
}
