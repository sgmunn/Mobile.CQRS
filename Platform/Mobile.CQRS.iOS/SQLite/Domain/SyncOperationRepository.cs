using System;
using System.Text.Json;
using Mobile.CQRS.Domain;

namespace Mobile.CQRS.SQLite.Domain;

public sealed class SyncOperationRecord
{
    [PrimaryKey]
    public Guid Identity { get; set; } // aggregate ID: one frozen operation per stream
    public Guid OperationId { get; set; }
    public string Data { get; set; }
}

public sealed class SyncOperationRepository : ISyncOperationRepository
{
    private readonly SQLiteConnection connection;
    public SyncOperationRepository(SQLiteConnection connection)
    {
        this.connection = connection;
        connection.CreateTable<SyncOperationRecord>();
    }
    public SyncOperation Get(Guid aggregateId)
    {
        var row = connection.Find<SyncOperationRecord>(aggregateId);
        return row == null ? null : JsonSerializer.Deserialize<SyncOperation>(row.Data);
    }
    public void Save(SyncOperation operation)
    {
        connection.Insert(new SyncOperationRecord {
            Identity = operation.Batch.AggregateId, OperationId = operation.Batch.OperationId,
            Data = JsonSerializer.Serialize(operation),
        });
    }
    public void Delete(Guid aggregateId, Guid operationId)
        => connection.Execute("delete from SyncOperationRecord where Identity = ? and OperationId = ?", aggregateId, operationId);
}
