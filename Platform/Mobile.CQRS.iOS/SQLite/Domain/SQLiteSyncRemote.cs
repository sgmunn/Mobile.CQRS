using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Serialization;

namespace Mobile.CQRS.SQLite.Domain;

public sealed class SyncReceipt
{
    [PrimaryKey]
    public Guid Identity { get; set; } // operation ID; retained for deduplication
    public string Fingerprint { get; set; }
}

/// <summary>A durable remote endpoint for local SQLite peers; network transports implement ISyncRemote.</summary>
public sealed class SQLiteSyncRemote : ISyncRemote
{
    private readonly Func<IUnitOfWorkScope> scopeFactory;
    private readonly ISerializer<IAggregateEvent> serializer;

    public string RemoteId { get; }

    public SQLiteSyncRemote(Func<IUnitOfWorkScope> scopeFactory, ISerializer<IAggregateEvent> serializer, string remoteId)
        { this.scopeFactory = scopeFactory; this.serializer = serializer; this.RemoteId = remoteId; }

    public Task<SyncStream> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopeFactory();
        var events = scope.GetRegisteredObject<IEventStore>().GetAllEvents(id);
        return Task.FromResult(new SyncStream(id, events.FirstOrDefault()?.AggregateType, SyncProtocol.Encode(events, serializer)));
    }

    public Task AppendAsync(SyncBatch batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (batch.OperationId == Guid.Empty) throw new InvalidOperationException("A sync operation ID is required.");
        var events = SyncProtocol.Decode(batch.AggregateId, batch.AggregateType, batch.Events, batch.ExpectedVersion, serializer);
        using var scope = scopeFactory();
        var connection = scope.GetRegisteredObject<SQLiteConnection>();
        connection.CreateTable<SyncReceipt>();
        var fingerprint = SyncProtocol.Fingerprint(batch);
        var receipt = connection.Find<SyncReceipt>(batch.OperationId);
        if (receipt != null)
        {
            if (receipt.Fingerprint != fingerprint) throw new ConflictException("Operation ID reused with different sync data.");
            return Task.CompletedTask;
        }
        var store = scope.GetRegisteredObject<IEventStore>();
        var version = store.GetCurrentVersion(batch.AggregateId);
        if (version != batch.ExpectedVersion)
            throw new ConcurrencyException(batch.AggregateId, batch.ExpectedVersion, version);
        var stream = connection.Table<AggregateEvent>().Where(x => x.AggregateId == batch.AggregateId)
            .OrderBy(x => x.Version).Take(1).ToList().FirstOrDefault();
        if (stream != null && stream.AggregateType != batch.AggregateType)
            throw new ConflictException("The aggregate ID belongs to a different aggregate type.");
        store.SaveEvents(batch.AggregateId, events, version);
        connection.Insert(new SyncReceipt { Identity = batch.OperationId, Fingerprint = fingerprint });
        cancellationToken.ThrowIfCancellationRequested();
        scope.Commit();
        return Task.CompletedTask;
    }
}
