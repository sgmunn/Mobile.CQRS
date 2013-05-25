using System;
using System.Collections.Generic;
using System.Json;
using Mobile.CQRS.Serialization;

namespace Mobile.CQRS.Domain.UnitTests
{
    public class TestAggregateRoot : AggregateRootBase<TestSnapshot>, IEventSourced
    {
        public TestAggregateRoot()
        {
        }

        public string Value1 { get; private set; }

        public int Value2 { get; private set; }

        public override ISnapshot GetSnapshot()
        {
            var snapshot = this.InternalState;
            snapshot.Identity = this.Identity;
            snapshot.Version = this.Version;
            return snapshot;
        }
        
        public override void LoadFromSnapshot(ISnapshot snapshot)
        {
            base.LoadFromSnapshot(snapshot);
            // for testing
            this.Value1 = ((TestSnapshot)snapshot).Value1;
            this.Value2 = ((TestSnapshot)snapshot).Value2;
        }

        public void Execute(TestCommand1 command)
        {
            this.RaiseEvent(command.Identity, new TestEvent1 { Value1 = command.Value1, Value2 = command.Value2, });
        }

        public void Apply(TestEvent1 domainEvent)
        {
            // these are for testing
            this.Value1 = domainEvent.Value1;
            this.Value2 = domainEvent.Value2;

            // this is for supporting snapshot
            this.InternalState.Value1 = domainEvent.Value1;
            this.InternalState.Value2 = domainEvent.Value2;
        }
    }
    
    public class TestSnapshot : ISnapshot
    {
        public Guid Identity { get; set; }
        
        public int Version { get; set; }
        
        public string Value1 { get; set; }
        
        public int Value2 { get; set; }
    }

    public class CommandBase : IAggregateCommand
    {
        public CommandBase()
        {
            this.Identity = Guid.NewGuid();
        }
        
        public Guid AggregateId { get; set; }
        
        public Guid Identity { get; set; }
    }
    
    public class TestCommand1 : CommandBase
    {
        public string Value1 { get; set; }
        public int Value2 { get; set; }
    }

    public class EventBase : IAggregateEvent
    {
        public EventBase()
        {
            this.Identity = Guid.NewGuid();
        }
        
        public Guid Identity { get; set; }
        
        public Guid AggregateId { get; set; }
        
        public int Version { get; set; }
        
        public DateTime Timestamp { get; set; } 

        public Guid CommandId { get; set; }
        
        public bool ConflictsWith(IAggregateEvent otherEvent)
        {
            return false;
        }
    }
    
    public class TestEvent1 : EventBase
    {
        public string Value1 { get; set; }
        public int Value2 { get; set; }
    }

    public class TestEventSerializer : ISerializer<IAggregateEvent>
    {
        public static TestEventSerializer Instance = new TestEventSerializer();

        private TestEventSerializer()
        {
        }

        public string SerializeToString(object instance)
        {
            // brain-dead serializer
            JsonValue json = new JsonObject(this.GetEventData((TestEvent1)instance));
            return json.ToString();
        } 

        public IAggregateEvent DeserializeFromString(string data)
        {
            var json = JsonValue.Parse(data);

            return new TestEvent1()
            {
                AggregateId = new Guid((string)json["AggregateId"]),
                Identity = new Guid((string)json["Identity"]),
                // not supported Timestamp = json["Timestamp"],
                Version = json["Version"],
                Value1 = json["Value1"],
                Value2 = json["Value2"],
            };
        } 

        private IEnumerable<KeyValuePair<string, JsonValue>> GetEventData(TestEvent1 domainEvent)
        {
            yield return new KeyValuePair<string, JsonValue>("AggregateId", domainEvent.AggregateId.ToString());
            yield return new KeyValuePair<string, JsonValue>("Identity", domainEvent.Identity.ToString());
            // not supported yield return new KeyValuePair<string, JsonValue>("Timestamp", domainEvent.Timestamp);
            yield return new KeyValuePair<string, JsonValue>("Version", domainEvent.Version);
            yield return new KeyValuePair<string, JsonValue>("Value1", domainEvent.Value1);
            yield return new KeyValuePair<string, JsonValue>("Value2", domainEvent.Value2);

        }
    }
}

