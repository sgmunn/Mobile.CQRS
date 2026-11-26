using System.Globalization;
using System.Text.Json;
using Mobile.CQRS;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Serialization;
using Mobile.CQRS.SQLite;

namespace Mobile.CQRS.Samples.OfflineSync;

public sealed class AdjustStock : IAggregateCommand
{
    public Guid Identity { get; set; } = Guid.NewGuid();
    public Guid AggregateId { get; set; }
    public int Delta { get; set; }
}

public sealed class StockAdjusted : IAggregateEvent
{
    public Guid Identity { get; set; }
    public Guid AggregateId { get; set; }
    public string AggregateType { get; set; } = "inventory.v1";
    public int Version { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid CommandId { get; set; }
    public int Delta { get; set; }
    public bool ConflictsWith(IAggregateEvent other) => true; // policy must explicitly approve replay
}

public sealed class StockSnapshot : ISnapshot
{
    public Guid Identity { get; set; }
    public int Version { get; set; }
    public int Quantity { get; set; }
}

[AggregateType("inventory.v1")]
public sealed class StockAggregate : AggregateRootBase<StockSnapshot>
{
    public int Quantity => InternalState.Quantity;
    public void Execute(AdjustStock command)
    {
        if (checked(Quantity + command.Delta) < 0) throw new InvalidOperationException("Stock cannot be negative.");
        RaiseEvent(command.Identity, new StockAdjusted { Delta = command.Delta });
    }
    public void Apply(StockAdjusted evt) => InternalState.Quantity = checked(Quantity + evt.Delta);
    public override ISnapshot GetSnapshot() => new StockSnapshot { Identity = Identity, Version = Version, Quantity = Quantity };
}

/// <summary>Portable sample format: stable names, UUID strings, UTC timestamps and explicit schema.</summary>
public sealed class StockEventSerializer : ISerializer<IAggregateEvent>
{
    public string SerializeToString(object value)
    {
        var evt = (StockAdjusted)value;
        return JsonSerializer.Serialize(new {
            eventId = evt.Identity.ToString("D"), aggregateId = evt.AggregateId.ToString("D"),
            aggregateType = evt.AggregateType, version = evt.Version, commandId = evt.CommandId.ToString("D"),
            timestamp = evt.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            eventType = "stock.adjusted.v1", schemaVersion = 1, payload = new { delta = evt.Delta },
        });
    }
    public IAggregateEvent DeserializeFromString(string data)
    {
        using var json = JsonDocument.Parse(data);
        var root = json.RootElement;
        if (root.GetProperty("eventType").GetString() != "stock.adjusted.v1" || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported stock event schema.");
        return new StockAdjusted {
            Identity = root.GetProperty("eventId").GetGuid(), AggregateId = root.GetProperty("aggregateId").GetGuid(),
            AggregateType = root.GetProperty("aggregateType").GetString(), Version = root.GetProperty("version").GetInt32(),
            CommandId = root.GetProperty("commandId").GetGuid(), Timestamp = root.GetProperty("timestamp").GetDateTime(),
            Delta = root.GetProperty("payload").GetProperty("delta").GetInt32(),
        };
    }
}

public sealed class JsonSerializerFor<T> : ISerializer<T>
{
    public string SerializeToString(object value) => JsonSerializer.Serialize((T)value);
    public T DeserializeFromString(string data) => JsonSerializer.Deserialize<T>(data);
}

public sealed class AdditiveStockPolicy : ISyncRebasePolicy
{
    public bool CanRebase(IList<IAggregateEvent> localChanges, IList<IAggregateEvent> remoteChanges)
        => localChanges.Concat(remoteChanges).All(evt => evt is StockAdjusted { Delta: >= 0 });
}

public sealed class StockProjection : IUniqueId
{
    [PrimaryKey]
    public Guid Identity { get; set; }
    public int Quantity { get; set; }
    public int Version { get; set; }
}

public sealed class StockProjector : ReadModelBuilder<StockProjection>
{
    public StockProjector(IRepository<StockProjection> repository) : base(repository) { }
    public void Apply(StockAdjusted evt)
    {
        var row = Repository.GetById(evt.AggregateId) ?? new StockProjection { Identity = evt.AggregateId };
        row.Quantity = checked(row.Quantity + evt.Delta);
        row.Version = evt.Version;
        Repository.Save(row);
    }
}
