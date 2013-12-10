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

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /* This class is repsonsible for merging changes from _the_ remote event store into our 
     * event store.
     * 
     * Load events up to last sync version.
     * Check for conflicts with unsynced events and new remote events. << checks our changes against others changes 
     * Add new remote events from sync version (get aggregate up to date with remote).
     * Execute pending commands and catch uncomitted events.
     * update eventstore with new events passing last sync version, current version to ensure concurrency
     * rebuild read models for aggregate
     * 
     */

    public sealed class SyncAgent : ISyncAgent
    {
        private readonly IEventStore remoteEventStore;

        private readonly IMergableEventStore localEventStore;

        private readonly IRepository<ISyncState> syncStateRepository;

        private readonly IPendingCommandRepository pendingCommands;

        private readonly ISnapshotRepository snapshotRepository;

        public SyncAgent(IMergableEventStore localEventStore, 
                         IEventStore remoteEventStore, 
                         IRepository<ISyncState> syncStateRepository, 
                         IPendingCommandRepository pendingCommands,
                         ISnapshotRepository snapshotRepository)
        {
            this.localEventStore = localEventStore;
            this.remoteEventStore = remoteEventStore;
            this.syncStateRepository = syncStateRepository;
            this.pendingCommands = pendingCommands;
            this.snapshotRepository = snapshotRepository;
        }

        public async Task<bool> SyncWithRemoteAsync<T>(Guid aggregateId) where T : class, IAggregateRoot, new()
        {
            var currentVersion = await this.localEventStore.GetCurrentVersionAsync(aggregateId).ConfigureAwait(false);

            var syncState = await this.syncStateRepository.GetByIdAsync(aggregateId).ConfigureAwait(false);

            // if no sync state, then assume that the aggregate originated from here, or other repo and that this is the first sync
            if (syncState == null)
            {
                syncState = this.syncStateRepository.New();
                syncState.Identity = aggregateId;
                syncState.AggregateType = AggregateRootBase.GetAggregateTypeDescriptor<T>();
            }

            // TODO: we can await these in parallel
            var commonEvents = await this.localEventStore.GetEventsUpToVersionAsync(aggregateId, syncState.LastSyncedVersion).ConfigureAwait(false);
            var newRemoteEvents = await this.remoteEventStore.GetEventsAfterVersionAsync(aggregateId, syncState.LastSyncedVersion).ConfigureAwait(false);
            var unsyncedLocalEvents = await this.localEventStore.GetEventsAfterVersionAsync(aggregateId, syncState.LastSyncedVersion).ConfigureAwait(false);
            var newCommonHistory = commonEvents.Concat(newRemoteEvents).ToList();

            var currentRemoteVersion = newRemoteEvents.Count > 0 ? newRemoteEvents.Last().Version : syncState.LastSyncedVersion;

            if (newRemoteEvents.Count == 0 && unsyncedLocalEvents.Count == 0)
            {
                // ain't nothin' to merge (in either direction) - all done
                return false;
            }

            // check for conflicts, anything we have done that conflicts with things that others have done
            var hasConflicts = unsyncedLocalEvents.Any(local => newRemoteEvents.Any(remote => local.ConflictsWith(remote)));
            if (hasConflicts)
            {
                throw new ConflictException("Pending commands conflict with new remote events");
            }

            // get the changes that we have that need to be sent to remote, either from pending commands
            // or because we've never synced at all
            var pendingEvents = await this.GetPendingEventsAsync<T>(syncState, newCommonHistory).ConfigureAwait(false);

            this.pendingCommands.RemovePendingCommands(aggregateId);

            // we need to do a full rebuild if we have previously synced with remote and we merged in any remote events
            var needsFullRebuild = syncState.LastSyncedVersion > 0 && newRemoteEvents.Count != 0;

            // if we're receiving an aggregate with changes and we have no changes, this fails
            var newVersion = pendingEvents.Count > 0 ? pendingEvents.Last().Version : currentRemoteVersion;

            // merge remote events into our eventstore - including our pending events
            if (newRemoteEvents.Count != 0)
            {
                this.localEventStore.MergeEvents(aggregateId, newRemoteEvents.Concat(pendingEvents).ToList(), currentVersion, syncState.LastSyncedVersion);
            }

            // update sync state
            syncState.LastSyncedVersion = newVersion;
            await this.syncStateRepository.SaveAsync(syncState).ConfigureAwait(false);

            // update remote
            if (pendingEvents.Count != 0)
            {
                await this.remoteEventStore.SaveEventsAsync(aggregateId, pendingEvents, currentRemoteVersion).ConfigureAwait(false);
            }

            return needsFullRebuild;
        }

        private async Task<List<IAggregateEvent>> GetPendingEventsAsync<T>(ISyncState syncState, IList<IAggregateEvent> remoteEvents) where T : class, IAggregateRoot, new()
        {
            if (syncState.LastSyncedVersion == 0)
            {
                // if we've never synced, then this aggregate must have been created locally, any events either in 
                // the event store or from pending commands are pending
                return (await this.localEventStore.GetAllEventsAsync(syncState.Identity).ConfigureAwait(false)).ToList();
            }

            // if we have synced the aggregate at least once, then we need to get events from pending commands
            var pendingCommandsToExecute = this.pendingCommands.PendingCommandsForAggregate(syncState.Identity).ToList();
            if (pendingCommandsToExecute.Count == 0)
            {
                return new List<IAggregateEvent>();
            }

            var aggregate = new T();
            aggregate.Identity = syncState.Identity;
            ((IEventSourced)aggregate).LoadFromEvents(remoteEvents);

            var exec = new ExecutingCommandExecutor(aggregate);
            await exec.ExecuteAsync(pendingCommandsToExecute, 0).ConfigureAwait(false);

            if (this.snapshotRepository != null && aggregate is ISnapshotSupport)
            {
                await this.snapshotRepository.SaveAsync(((ISnapshotSupport)aggregate).GetSnapshot()).ConfigureAwait(false);
            }

            return aggregate.UncommittedEvents.ToList();
        }
    }
}