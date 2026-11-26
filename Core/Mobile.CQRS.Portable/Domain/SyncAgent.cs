// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SyncAgent.cs" company="sgmunn">
//   (c) sgmunn 2013  
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
using System.Threading;
using System.Threading.Tasks;
using Mobile.CQRS.Serialization;

namespace Mobile.CQRS.Domain;

/// <summary>Prepare durably, send without a local transaction, then finalize atomically.</summary>
public sealed class SyncAgent : ISyncAgent
{
    private readonly Func<IUnitOfWorkScope> scopeFactory;
    private readonly ISyncRemote remote;
    private readonly ISerializer<IAggregateEvent> serializer;

    public SyncAgent(Func<IUnitOfWorkScope> scopeFactory, ISyncRemote remote, ISerializer<IAggregateEvent> serializer)
        { this.scopeFactory = scopeFactory; this.remote = remote; this.serializer = serializer; }

    public bool SyncWithRemote<T>(Guid aggregateId) where T : class, IAggregateRoot, new()
        => SyncWithRemoteAsync<T>(aggregateId).GetAwaiter().GetResult().HistoryChanged;

    public async Task<SyncResult> SyncWithRemoteAsync<T>(Guid aggregateId, ISyncRebasePolicy policy = null,
        CancellationToken cancellationToken = default) where T : class, IAggregateRoot, new()
    {
        if (aggregateId == Guid.Empty || string.IsNullOrWhiteSpace(remote.RemoteId))
            throw new ArgumentException("Sync requires an aggregate ID and a stable remote identity.");
        var type = AggregateRootBase.GetAggregateTypeDescriptor<T>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyncOperation operation;
            SyncEvent[] local;
            IList<IAggregateCommand> commands;
            int synced;
            using (var scope = scopeFactory())
            {
                operation = Journal(scope).Get(aggregateId);
                var state = States(scope).GetById(aggregateId);
                if (state != null && (state.AggregateType != type ||
                    (state.RemoteId != null && state.RemoteId != remote.RemoteId)))
                    throw new ConflictException("This stream is bound to a different aggregate type or remote.");
                synced = state?.LastSyncedVersion ?? 0;
                local = SyncProtocol.Encode(Store(scope).GetAllEvents(aggregateId), serializer);
                commands = Commands(scope).PendingCommandsForAggregate(aggregateId);
            }
            SyncProtocol.Decode(aggregateId, type, local, 0, serializer);
            if (operation == null)
            {
                var history = await remote.ReadAsync(aggregateId, cancellationToken).ConfigureAwait(false);
                if (history.AggregateId != aggregateId || (history.AggregateType != null && history.AggregateType != type))
                    throw new ConflictException("Remote returned a different aggregate or type.");
                SyncProtocol.Decode(aggregateId, type, history.Events, 0, serializer);
                operation = Prepare<T>(aggregateId, type, local, commands, synced, history.Events, policy);
                cancellationToken.ThrowIfCancellationRequested();
                using var scope = scopeFactory();
                var existing = Journal(scope).Get(aggregateId);
                if (existing != null) operation = existing;
                else
                {
                    var now = SyncProtocol.Encode(Store(scope).GetAllEvents(aggregateId), serializer);
                    var latestState = States(scope).GetById(aggregateId);
                    if (latestState?.RemoteId != null && latestState.RemoteId != remote.RemoteId)
                        throw new ConflictException("The stream was concurrently bound to a different remote.");
                    if (!now.SequenceEqual(local) || (latestState?.LastSyncedVersion ?? 0) != synced)
                        throw new ConcurrencyException(aggregateId, local.Length, now.Length);
                    Journal(scope).Save(operation);
                }
                scope.Commit();
            }
            if (operation.RemoteId != remote.RemoteId || operation.Batch.AggregateType != type)
                throw new ConflictException("The pending operation belongs to a different remote or aggregate type.");
            try
            {
                if (operation.Batch.Events.Length != 0)
                    await remote.AppendAsync(operation.Batch, cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyException)
            {
                // Only this contractually definitive rejection permits replanning.
                // Timeouts, cancellation and lost responses retain the exact request.
                using var scope = scopeFactory();
                Journal(scope).Delete(aggregateId, operation.Batch.OperationId);
                scope.Commit();
                if (attempt == 2) throw;
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Finalize<T>(operation, policy);
        }
        throw new InvalidOperationException("Sync retry budget exhausted.");
    }

    private SyncOperation Prepare<T>(Guid id, string type, SyncEvent[] local, IList<IAggregateCommand> commands,
        int synced, SyncEvent[] remoteHistory, ISyncRebasePolicy policy) where T : class, IAggregateRoot, new()
    {
        if (synced < 0 || synced > local.Length || synced > remoteHistory.Length
            || !local.Take(synced).SequenceEqual(remoteHistory.Take(synced)))
            throw new ConflictException("Acknowledged history was truncated or rewritten.");
        var common = synced;
        while (common < Math.Min(local.Length, remoteHistory.Length) && local[common] == remoteHistory[common]) common++;
        if (synced == 0 && local.Length > 0 && remoteHistory.Length > 0 && common == 0)
            throw new ConflictException("First sync has divergent history with no proven common ancestry.");
        var localTail = local.Skip(common).ToArray();
        var remoteTail = remoteHistory.Skip(common).ToArray();
        var candidate = localTail;
        if (localTail.Length > 0 && remoteTail.Length > 0)
        {
            var tailEvents = SyncProtocol.Decode(id, type, localTail, common, serializer);
            var prefixCommands = SyncProtocol.Decode(id, type, local.Take(common).ToArray(), 0, serializer)
                .Select(x => x.CommandId).ToHashSet();
            if (tailEvents.Any(x => prefixCommands.Contains(x.CommandId)))
                throw new ConflictException("Cannot replay a partially acknowledged command.");
            candidate = Rebase<T>(id, type, remoteHistory, tailEvents, commands,
                SyncProtocol.Decode(id, type, remoteTail, common, serializer), policy);
        }
        var batch = new SyncBatch(Guid.NewGuid(), id, type, remoteHistory.Length, candidate);
        SyncProtocol.Decode(id, type, candidate, remoteHistory.Length, serializer);
        return new SyncOperation(batch, local, remoteHistory.Concat(candidate).ToArray(),
            commands.Select(x => x.Identity).Distinct().ToArray(), synced, remote.RemoteId);
    }

    private SyncEvent[] Rebase<T>(Guid id, string type, SyncEvent[] canonical, IList<IAggregateEvent> localChanges,
        IList<IAggregateCommand> commands, IList<IAggregateEvent> remoteChanges, ISyncRebasePolicy policy)
        where T : class, IAggregateRoot, new()
    {
        if (policy == null || !policy.CanRebase(localChanges, remoteChanges))
            throw new ConflictException("Concurrent changes require an explicit command replay policy.");
        var ids = localChanges.Select(x => x.CommandId).ToHashSet();
        var replay = commands.Where(x => ids.Contains(x.Identity)).ToList();
        if (ids.Contains(Guid.Empty) || !ids.IsSubsetOf(replay.Select(x => x.Identity))
            || replay.Any(x => x.AggregateId != id))
            throw new ConflictException("Pending commands do not cover the speculative history.");
        var aggregate = new T { Identity = id };
        ((IEventSourced)aggregate).LoadFromEvents(SyncProtocol.Decode(id, type, canonical, 0, serializer));
        new ExecutingCommandExecutor(aggregate).Execute(replay, 0);
        var result = SyncProtocol.Encode(aggregate.UncommittedEvents, serializer);
        SyncProtocol.Decode(id, type, result, canonical.Length, serializer);
        return result;
    }

    private SyncResult Finalize<T>(SyncOperation operation, ISyncRebasePolicy policy) where T : class, IAggregateRoot, new()
    {
        var batch = operation.Batch;
        using var scope = scopeFactory();
        var active = Journal(scope).Get(batch.AggregateId);
        if (active == null) return new SyncResult(batch.OperationId,
            States(scope).GetById(batch.AggregateId)?.LastSyncedVersion ?? 0, false, 0);
        if (active.Batch.OperationId != batch.OperationId)
            throw new ConflictException("Another sync operation owns this stream.");
        var current = SyncProtocol.Encode(Store(scope).GetAllEvents(batch.AggregateId), serializer);
        SyncProtocol.Decode(batch.AggregateId, batch.AggregateType, current, 0, serializer);
        if (current.Length < operation.PreparedHistory.Length
            || !current.Take(operation.PreparedHistory.Length).SequenceEqual(operation.PreparedHistory))
            throw new ConflictException("Local prepared history changed; the accepted operation is retained for recovery.");
        var final = operation.CanonicalHistory;
        if (final.SequenceEqual(operation.PreparedHistory)) final = current; // preserve later local commands and event IDs
        else if (current.Length > operation.PreparedHistory.Length)
        {
            var extra = SyncProtocol.Decode(batch.AggregateId, batch.AggregateType,
                current.Skip(operation.PreparedHistory.Length).ToArray(), operation.PreparedHistory.Length, serializer);
            var pending = Commands(scope).PendingCommandsForAggregate(batch.AggregateId)
                .Where(x => !operation.CommandIds.Contains(x.Identity)).ToList();
            var common = 0;
            while (common < Math.Min(final.Length, current.Length) && final[common] == current[common]) common++;
            var incoming = SyncProtocol.Decode(batch.AggregateId, batch.AggregateType,
                final.Skip(common).ToArray(), common, serializer);
            final = final.Concat(Rebase<T>(batch.AggregateId, batch.AggregateType, final, extra, pending, incoming, policy)).ToArray();
        }
        var changed = !current.SequenceEqual(final);
        if (changed)
        {
            var common = 0;
            while (common < Math.Min(current.Length, final.Length) && current[common] == final[common]) common++;
            var tail = SyncProtocol.Decode(batch.AggregateId, batch.AggregateType, final.Skip(common).ToArray(), common, serializer);
            Store(scope).MergeEvents(batch.AggregateId, tail, current.Length, common);
            scope.GetRegisteredObject<ISnapshotRepository>()?.Invalidate(batch.AggregateId);
        }
        var states = States(scope);
        var state = states.GetById(batch.AggregateId) ?? states.New();
        state.Identity = batch.AggregateId;
        state.AggregateType = batch.AggregateType;
        state.RemoteId = operation.RemoteId;
        state.LastSyncedVersion = operation.CanonicalHistory.Length;
        states.Save(state);
        Commands(scope).RemovePendingCommands(batch.AggregateId, operation.CommandIds);
        Journal(scope).Delete(batch.AggregateId, batch.OperationId);
        scope.Commit();
        return new SyncResult(batch.OperationId, state.LastSyncedVersion, changed, batch.Events.Length);
    }

    private static IMergableEventStore Store(IUnitOfWorkScope scope) => scope.GetRegisteredObject<IMergableEventStore>()
        ?? throw new InvalidOperationException("Sync requires a mergeable local store.");
    private static IRepository<ISyncState> States(IUnitOfWorkScope scope) => scope.GetRegisteredObject<IRepository<ISyncState>>()
        ?? throw new InvalidOperationException("Configure command serialization before syncing.");
    private static IPendingCommandRepository Commands(IUnitOfWorkScope scope) => scope.GetRegisteredObject<IPendingCommandRepository>()
        ?? throw new InvalidOperationException("Sync requires durable pending commands.");
    private static ISyncOperationRepository Journal(IUnitOfWorkScope scope) => scope.GetRegisteredObject<ISyncOperationRepository>()
        ?? throw new InvalidOperationException("Sync requires a durable operation journal.");
}
