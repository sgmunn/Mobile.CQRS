using System;
using Mobile.CQRS.Domain;

namespace Mobile.CQRS.SQLite.Domain;

public sealed class ReadModelCheckpointRecord
{
    [PrimaryKey]
    public Guid Identity { get; set; }
    public int Version { get; set; }
    public Guid DeliveryId { get; set; }
}

public sealed class ReadModelCheckpointStore : IReadModelCheckpointStore
{
    private readonly SQLiteConnection connection;

    public ReadModelCheckpointStore(SQLiteConnection connection)
    {
        this.connection = connection;
        connection.CreateTable<ReadModelCheckpointRecord>();
    }

    public ReadModelCheckpoint Get(Guid aggregateId)
    {
        var row = connection.Find<ReadModelCheckpointRecord>(aggregateId);
        return row == null ? null : new ReadModelCheckpoint(row.Version, row.DeliveryId);
    }

    public void Save(Guid aggregateId, int version, Guid deliveryId)
    {
        if (!connection.IsInTransaction)
            throw new InvalidOperationException("Checkpoint must commit with projection output.");
        var row = new ReadModelCheckpointRecord { Identity = aggregateId, Version = version, DeliveryId = deliveryId };
        if (connection.Update(row) == 0) connection.Insert(row);
    }
}
