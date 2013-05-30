using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

// TODO: Use lastest SQLite with Async
// TODO: Mobile.Mvvm view model with support for loading changes while being editing

using System.Reflection;
using System.Collections.Concurrent;
using Mobile.CQRS.SQLite.Domain;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.Serialization;


namespace Mobile.CQRS.Domain
{
    // TODO: Test sqlite max length of strings, doesn't seem to be 140

    // TODO: read model builders need to be queue / pointer based

    // -- need to be able to poll work items, get events for each, get read model builders and run
    // -- must run in background thread
    // -- must be cancellable and be able to be poked with some events


    // TODO: async SQLite

    // TODO: test coverage and in memory support - must handle uow in event store

    // TODO: seal classes where needed

    // TODO: get read model builders to take a list of events instead of one at a time

    // TODO: update snapshot after sync

    // TODO: that might be all for now


    /*
     *  - pull from the queue of work items, get events for aggregate >= version
     *  - remove from work item only removes if version is the same, otherwise leaves it in the queue
     *  - when building, the read model needs a version on it
     *  - builder doesn't process events prior to the version of the read model 
     *  - builder must set version correctly in the read model, if it handled the event
     *  - this means we can skip events, 
     *  - queue item indicates that there is work to do
     *  
     *  - read model queue is in its own transaction, except for when we first add events the the event store, this needs to be transactionally consistent
     *    otherwise we have a possibility of missing events if we crash in between transactions
     * 
     *  - read model building should be idempotent
     *  - this strategy is a little chatty with respect to grabbing work
     *  - however, we wouldn't expect a lot of work items to do anyway, usually the one
     * 
     * 
     * 
     */ 


    public interface IReadModelWorkItem
    {
        Guid AggregateId { get; set; }
        string AggregateType { get; set; }
        int FromVersion { get; set; }
    }
    
    public interface IReadModelQueue
    {
        void Enqueue(Guid aggregateId, string aggregateType, int fromVersion);
        void Dequeue(IReadModelWorkItem workItem);
        IList<IReadModelWorkItem> Peek(int batchSize);
    }



    public class Test
    {

        public void X()
        {



        }
    }






}