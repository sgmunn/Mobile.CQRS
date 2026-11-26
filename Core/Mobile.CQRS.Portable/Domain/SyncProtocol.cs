using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mobile.CQRS.Serialization;

namespace Mobile.CQRS.Domain;

// Serialized payloads are frozen before sending. Both peers must use a compatible,
// deterministic serializer. Event identity and payload may not change on retries.
public sealed record SyncEvent(Guid Identity, int Version, string Data);
public sealed record SyncStream(Guid AggregateId, string AggregateType, SyncEvent[] Events);
public sealed record SyncBatch(Guid OperationId, Guid AggregateId, string AggregateType, int ExpectedVersion, SyncEvent[] Events);
public sealed record SyncResult(Guid OperationId, int SyncedVersion, bool HistoryChanged, int UploadedEvents);
public sealed record SyncOperation(SyncBatch Batch, SyncEvent[] PreparedHistory, SyncEvent[] CanonicalHistory,
    Guid[] CommandIds, int PreviousSyncedVersion, string RemoteId);

public interface ISyncRemote
{
    string RemoteId { get; }
    Task<SyncStream> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default);
    // Append and operation receipt MUST commit atomically. Check the receipt first,
    // even if the stream advanced. ConcurrencyException means definitely not accepted.
    Task AppendAsync(SyncBatch batch, CancellationToken cancellationToken = default);
}

public interface ISyncOperationRepository
{
    SyncOperation Get(Guid aggregateId);
    void Save(SyncOperation operation);
    void Delete(Guid aggregateId, Guid operationId);
}

public interface ISyncRebasePolicy
{
    // Opt in only for commands safe to replay against this canonical history.
    bool CanRebase(IList<IAggregateEvent> localChanges, IList<IAggregateEvent> remoteChanges);
}

public static class SyncProtocol
{
    public static SyncEvent[] Encode(IEnumerable<IAggregateEvent> events, ISerializer<IAggregateEvent> serializer)
        => events.Select(x => new SyncEvent(x.Identity, x.Version, serializer.SerializeToString(x))).ToArray();

    public static IAggregateEvent[] Decode(Guid id, string type, SyncEvent[] events, int afterVersion,
        ISerializer<IAggregateEvent> serializer)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(type) || events == null || afterVersion < 0)
            throw new InvalidOperationException("Invalid sync stream metadata.");
        var result = new List<IAggregateEvent>();
        var identities = new HashSet<Guid>();
        var version = afterVersion;
        foreach (var envelope in events)
        {
            if (envelope == null || envelope.Identity == Guid.Empty || !identities.Add(envelope.Identity)
                || envelope.Version != checked(++version) || envelope.Data == null)
                throw new InvalidOperationException("Sync events must have unique identities and contiguous versions.");
            var evt = serializer.DeserializeFromString(envelope.Data);
            if (evt == null || evt.Identity != envelope.Identity || evt.AggregateId != id
                || evt.AggregateType != type || evt.Version != envelope.Version)
                throw new InvalidOperationException("Sync payload does not match its envelope or aggregate.");
            result.Add(evt);
        }
        return result.ToArray();
    }

    public static string Fingerprint(SyncBatch batch)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(batch)));
}

/// <summary>Compatibility for stores whose SaveEvents is already a durable atomic append.</summary>
public sealed class EventStoreSyncRemote : ISyncRemote
{
    private readonly IEventStore store;
    private readonly ISerializer<IAggregateEvent> serializer;

    public string RemoteId { get; }

    public EventStoreSyncRemote(IEventStore store, ISerializer<IAggregateEvent> serializer, string remoteId)
        { this.store = store; this.serializer = serializer; this.RemoteId = remoteId; }

    public Task<SyncStream> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var events = store.GetAllEvents(id);
        return Task.FromResult(new SyncStream(id, events.FirstOrDefault()?.AggregateType, SyncProtocol.Encode(events, serializer)));
    }

    public async Task AppendAsync(SyncBatch batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (batch.OperationId == Guid.Empty) throw new InvalidOperationException("A sync operation ID is required.");
        var events = SyncProtocol.Decode(batch.AggregateId, batch.AggregateType, batch.Events, batch.ExpectedVersion, serializer);
        var remote = await ReadAsync(batch.AggregateId, cancellationToken).ConfigureAwait(false);
        // Frozen event IDs/payloads are a durable deduplication key for legacy
        // append-only stores, including a response lost after the original append.
        if (remote.Events.Length >= batch.ExpectedVersion + events.Length
            && remote.Events.Skip(batch.ExpectedVersion).Take(events.Length).SequenceEqual(batch.Events)) return;
        if (remote.Events.Length != batch.ExpectedVersion)
            throw new ConcurrencyException(batch.AggregateId, batch.ExpectedVersion, remote.Events.Length);
        store.SaveEvents(batch.AggregateId, events, batch.ExpectedVersion);
    }
}
