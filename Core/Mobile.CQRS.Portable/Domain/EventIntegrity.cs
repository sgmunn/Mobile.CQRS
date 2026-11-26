using System;
using System.Collections.Generic;

namespace Mobile.CQRS.Domain;

/// <summary>The shared append/replay contract. Validate the whole batch before applying it.</summary>
public static class EventIntegrity
{
    public static void ValidateBatch(Guid aggregateId, string aggregateType, IList<IAggregateEvent> events, int afterVersion)
    {
        if (aggregateId == Guid.Empty) throw new ArgumentException("An aggregate ID is required.", nameof(aggregateId));
        if (afterVersion < 0) throw new ArgumentOutOfRangeException(nameof(afterVersion));
        ArgumentNullException.ThrowIfNull(events);
        if (string.IsNullOrWhiteSpace(aggregateType) && events.Count != 0)
            throw new InvalidOperationException("An aggregate type is required.");
        var identities = new HashSet<Guid>();
        var version = afterVersion;
        foreach (var evt in events)
        {
            if (evt == null || evt.Identity == Guid.Empty || !identities.Add(evt.Identity)
                || evt.AggregateId != aggregateId || evt.AggregateType != aggregateType
                || evt.Version != checked(++version))
                throw new InvalidOperationException("Events require unique identities, a matching aggregate/type and contiguous versions.");
        }
    }
}
