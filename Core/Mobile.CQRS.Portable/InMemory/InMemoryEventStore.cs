// --------------------------------------------------------------------------------------------------------------------
// <copyright file="InMemoryEventStore.cs" company="sgmunn">
//   (c) sgmunn 2012  
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//   documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//   the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//   to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//   the Software.
// 
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//   THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//   CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//   IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mobile.CQRS.Serialization;

namespace Mobile.CQRS.Domain;

public sealed class InMemoryEventStore : IMergableEventStore
{
    private sealed record StoredEvent(Guid Identity, Guid AggregateId, string AggregateType, int Version, Type RuntimeType, string Data);
    private readonly Dictionary<Guid, List<StoredEvent>> storage = new();
    private readonly ISerializer<IAggregateEvent> serializer;
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

    // Default JSON supports public properties/fields of concrete event types.
    // Supply the same serializer as SQLite for custom or polymorphic payloads.
    public InMemoryEventStore(ISerializer<IAggregateEvent> serializer = null) { this.serializer = serializer; }
    public void Dispose() { }

    public void SaveEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion)
    {
        lock (storage)
        {
            var existing = Rows(aggregateId);
            var type = existing.FirstOrDefault()?.AggregateType ?? events?.FirstOrDefault()?.AggregateType;
            CheckVersion(aggregateId, expectedVersion, existing.Count);
            EventIntegrity.ValidateBatch(aggregateId, type, events, expectedVersion);
            var frozen = events.Select(Freeze).ToList();
            RejectDuplicateIds(frozen, storage.Values.SelectMany(x => x));
            storage[aggregateId] = existing.Concat(frozen).ToList();
        }
    }

    public void MergeEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion, int afterVersion)
    {
        lock (storage)
        {
            var existing = Rows(aggregateId);
            CheckVersion(aggregateId, expectedVersion, existing.Count);
            if (afterVersion < 0 || afterVersion > existing.Count) throw new ArgumentOutOfRangeException(nameof(afterVersion));
            var type = existing.FirstOrDefault()?.AggregateType ?? events?.FirstOrDefault()?.AggregateType;
            EventIntegrity.ValidateBatch(aggregateId, type, events, afterVersion);
            var frozen = events.Select(Freeze).ToList();
            var retained = storage.Where(x => x.Key != aggregateId).SelectMany(x => x.Value).Concat(existing.Take(afterVersion));
            RejectDuplicateIds(frozen, retained);
            storage[aggregateId] = existing.Take(afterVersion).Concat(frozen).ToList();
        }
    }

    public int GetCurrentVersion(Guid id) { lock (storage) return Rows(id).Count; }
    public IList<IAggregateEvent> GetAllEvents(Guid id) { lock (storage) return Rows(id).Select(Read).ToList(); }
    public IList<IAggregateEvent> GetEventsAfterVersion(Guid id, int version)
        => GetAllEvents(id).Where(x => x.Version > version).ToList();
    public IList<IAggregateEvent> GetEventsUpToVersion(Guid id, int version)
        => GetAllEvents(id).Where(x => x.Version <= version).ToList();

    private List<StoredEvent> Rows(Guid id) => storage.TryGetValue(id, out var rows) ? rows : new List<StoredEvent>();
    private StoredEvent Freeze(IAggregateEvent evt)
    {
        var row = new StoredEvent(evt.Identity, evt.AggregateId, evt.AggregateType, evt.Version, evt.GetType(),
            serializer == null ? JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions) : serializer.SerializeToString(evt));
        Read(row); // reject serializers that discard envelope metadata before storing anything
        return row;
    }
    private IAggregateEvent Read(StoredEvent row)
    {
        var evt = serializer == null ? (IAggregateEvent)JsonSerializer.Deserialize(row.Data, row.RuntimeType, JsonOptions)
            : serializer.DeserializeFromString(row.Data);
        if (evt == null || evt.Identity != row.Identity || evt.AggregateId != row.AggregateId
            || evt.AggregateType != row.AggregateType || evt.Version != row.Version)
            throw new InvalidOperationException("Serialized event metadata does not match its envelope.");
        return evt;
    }
    private static void CheckVersion(Guid id, int expected, int actual)
    {
        if (expected < 0) throw new ArgumentOutOfRangeException(nameof(expected));
        if (expected != actual) throw new ConcurrencyException(id, expected, actual);
    }
    private static void RejectDuplicateIds(IEnumerable<StoredEvent> incoming, IEnumerable<StoredEvent> retained)
    {
        var ids = retained.Select(x => x.Identity).ToHashSet();
        if (incoming.Any(x => !ids.Add(x.Identity))) throw new InvalidOperationException("Event identity already exists.");
    }
}
