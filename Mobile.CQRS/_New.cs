using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

// TODO: Mobile.Mvvm view model with support for loading changes while being editing

using System.Reflection;

//TODO: our read model builder base is tied to SQLite, which means that we can't do in memory read model building
// we need a way to pass in the IRepository as well as a means by which to delete for an aggregate
// read model implementations need to be storage agnostic

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

namespace Mobile.CQRS.Domain
{
}