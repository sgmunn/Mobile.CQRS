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
 * 
 * 
 */ 

namespace Mobile.CQRS.Data
{
}