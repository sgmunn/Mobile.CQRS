using System;

namespace Mobile.CQRS.Domain;

/// <summary>Checkpoint for all delayed builders of an aggregate, in the projection transaction.</summary>
public interface IReadModelCheckpointStore
{
    ReadModelCheckpoint Get(Guid aggregateId);
    void Save(Guid aggregateId, int version, Guid deliveryId);
}

public sealed record ReadModelCheckpoint(int Version, Guid DeliveryId);
