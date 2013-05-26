using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

// TODO: Use lastest SQLite with Async
// TODO: Mobile.Mvvm view model with support for loading changes while being editing

using System.Reflection;


namespace Mobile.CQRS.Domain
{



    public class MergeManager 
    {
        /* This class is repsonsible for merging changes from _the_ remote event store into our 
         * event store.
         * 
         * Load events up to last sync version.
         * Add new remote events from sync version (get aggregate up to date with remote).
         * Execute pending commands and catch uncomitted events.
         * Check for conflicts with uncommitted events and new remote events.
         * if no conflicts, update eventstore with new events passing last sync version, current version to ensure concurrency
         * rebuild read models for aggregate
         * 
         */

        public IEventStore RemoteEventStore { get; private set; }
        
        public IEventStore LocalEventStore { get; private set; }

        public IRepository<ISyncState> SyncState { get; private set; }
        
        public IPendingCommandRepository PendingCommands { get; private set; }

        // do this in a unit of work
        public bool SyncWithRemote<T>(Guid aggregateId) where T : class, IAggregateRoot, new()
        {
            var currentVersion = this.LocalEventStore.GetCurrentVersion(aggregateId);

            var syncState = this.SyncState.GetById(aggregateId);

            var commonEvents = this.LocalEventStore.GetEventsUpToVersion(aggregateId, syncState.LastSyncedVersion);

            var newRemoteEvents = this.RemoteEventStore.GetEventsAfterVersion(aggregateId, syncState.LastSyncedVersion).ToList();

            if (newRemoteEvents.Count == 0 && currentVersion == syncState.LastSyncedVersion)
            {
                // ain't nothin' to merge (in either direction)
                return false;
            }

            var aggregate = new T();
            aggregate.Identity = aggregateId;
            var pendingEvents = new List<IAggregateEvent>();

            var pendingCommands = this.PendingCommands.PendingCommandsForAggregate(aggregateId).ToList();
            if (pendingCommands.Count > 0)
            {
                ((IEventSourced)aggregate).LoadFromEvents(commonEvents);

                var exec = new ExecutingCommandExecutor(aggregate);
                exec.Execute(pendingCommands, 0);

                pendingEvents.AddRange(aggregate.UncommittedEvents.ToList());

                var hasConflicts = pendingEvents.Any(local => newRemoteEvents.Any(remote => local.ConflictsWith(remote)));
                if (hasConflicts)
                {
                    throw new ConflictException("Pending commands conflict with new remote events");
                }
            }

            // TODO: try to update the remote store last, we need our local commit to succeed if it succeeds
            // otherwise, what state are we in??

            if (pendingEvents.Count > 0)
            {
                var currentRemoteVersion = newRemoteEvents.Count > 0 ? newRemoteEvents.Last().Version : syncState.LastSyncedVersion;

                this.RemoteEventStore.SaveEvents(aggregateId, pendingEvents, currentRemoteVersion);
            }

            // TODO: can we delete all and rely on the transaction rolling back if concurrency?
            this.PendingCommands.RemovePendingCommands(aggregateId);

            newRemoteEvents.AddRange(pendingEvents);

            // because the event store doesn't do this for us, we need to 
            // this is essentially our concurrency check that we haven't added another pending command to the queue

            // merge events back to local event store
            this.LocalEventStore.MergeEvents(aggregateId, newRemoteEvents, currentVersion, syncState.LastSyncedVersion);

            syncState.LastSyncedVersion = newRemoteEvents.Last().Version;
            this.SyncState.Save(syncState);

            // rebuild .. if we had no pending commands, then we can simply do a build not a rebuild
            // return true for full rebuild, false for partial, but from what version
            // if we have no pending commands then build read models from their current state 
            return pendingCommands.Count > 0;
        }
    }


}




