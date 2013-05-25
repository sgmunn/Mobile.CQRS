using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

// TODO: Mobile.Mvvm view model with support for loading changes while being editing

using System.Reflection;

/*
 * Sync!
 * 
 * 2 clients updating a common aggregate
 * the common event store will have to support concurrency
 * thus, when we merge we will either incorporate the remote events with our own, or we will discard our events
 *  -- both options require us to be able to rebuild a read model from scratch
 *  -- 
 * 
 * locally read models are always based on our local events
 * we update a sync event id to indicate which events afterwards need to be merged
 * 
 * IAggregateEvent.ConflictsWith(IAggregateEvent)
 * 
 * we might need a pending events queue
 * 
 * if no conflicts, 
 *   add server events to eventstore from sync verion
 *   rebuild read models
 *   rerun commands
 *     store events, update read models
 * 
 * do we need to add an autoinc to the eventstore to ensure a global sequence??
 * likewise PendingCommandQueue
 * 
 * for events from the remote server, do we simply create an event store for the remote events??
 *  -- easy to work with, duplicates data???
 *  -- add an extra method to delelete prior to an event id
 * RemoteEventContract and RemoteEventStore, 
 * 
 * -- try it out like that.
 * 
 * 
 * 
 * we will need a queue of pending events, which we need to get in order grouped by command id and aggregate
 * we will need a queue of pending commands, which we need to get in order
 * we will need to be able to rebuild read models
 *      -- how do we delete all the read models for a given aggregate
 *      -- this will have to be a method on the read model builder
 *      -- we will need to publish read model changes, deletes, changes, additions etc and there could be lots depending on
 *         the read model (eg transactions / item lines)
 * 
 * background process to get events from last synced version + events in queue for a given aggregate
 *      -- store in pending events
 * 
 * when we execure commands, we need to store the command in the queue
 * 
 * we will need to be able to publish more than one event at a time to a bus I think?
 * 
 */
using Mobile.CQRS.Data; 

namespace Mobile.CQRS.Domain
{
    public interface ISyncState : IUniqueId
    {
        int LastSyncedVersion { get; }

    }

    public interface IPendingCommands : IRepository<IAggregateCommand>
    {
        IList<IAggregateCommand> PendingCommandsForAggregate(Guid id);
        void RemovePendingCommands(IList<IAggregateCommand> commands);
    }

    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message)
        {
        }
    }

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
        
        public IPendingCommands PendingCommands { get; private set; }

        public IAggregateManifestRepository LocalManifest { get; private set; }
        
        public IAggregateManifestRepository RemoteManifest { get; private set; }

        // TODO: can we get rid of the external requirement to interact with the manifest - can we move this to the eventstore instead!!!!!!!

        // ISnapshotRepo needs a methid to Save(with expeced version);

        // TODO: event store needs to have expected version for concurrency check in save events.

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

                var exec = new ExecutingCommandExecutor<T>(aggregate);
                exec.Execute(pendingCommands, 0);

                pendingEvents.AddRange(aggregate.UncommittedEvents.ToList());

                var hasConflicts = pendingEvents.Any(local => newRemoteEvents.Any(remote => local.ConflictsWith(remote)));
                if (hasConflicts)
                {
                    throw new ConflictException("Pending commands conflict with new remote events");
                }
            }

            if (pendingEvents.Count > 0)
            {
                var currentRemoteVersion = newRemoteEvents.Count > 0 ? newRemoteEvents.Last().Version : syncState.LastSyncedVersion;
                this.RemoteManifest.UpdateManifest(aggregate.AggregateType, aggregateId, currentRemoteVersion, pendingEvents.Last().Version);

                this.RemoteEventStore.SaveEvents(aggregateId, pendingEvents);
            }

            this.PendingCommands.RemovePendingCommands(pendingCommands);

            newRemoteEvents.AddRange(pendingEvents);

            // because the event store doesn't do this for us, we need to 
            // this is essentially our concurrency check that we haven't added another pending command to the queue
            this.LocalManifest.UpdateManifest(aggregate.AggregateType, aggregateId, currentVersion, newRemoteEvents.Last().Version);

            // merge events back to local event store
            this.LocalEventStore.MergeEvents(aggregateId, newRemoteEvents, syncState.LastSyncedVersion);

            // rebuild .. if we had no pending commands, then we can simply do a build not a rebuild
            // return true for full rebuild, false for partial, but from what version
            // if we have no pending commands then build read models from their current state 
            return pendingCommands.Count > 0;
        }
    }


}




