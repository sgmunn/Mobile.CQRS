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

    // TODO: state sourced domains must have immediate read modelbuilding because you can't rebuild from events!!

    // TODO: async SQLite

    // TODO: test coverage and in memory support - must handle uow in event store

    // TODO: seal classes where needed

    // TODO: get read model builders to take a list of events instead of one at a time

    // TODO: update snapshot after sync

    // TODO: that might be all for now


    /*
     * read model builders
     * currently we have builders registered for each aggregate type
     * and we have this ability to share commands across aggregates
     * the event has the aggregate type on it, so we can use that to send it to the correct builder instances
     * --lookup type in registrations, get builders, send events
     * 
     * thus we could potentially have a pointer based on aggregate type
     * we would need to have the same state for all builders registered against that aggregate type
     * and would have to process all together.
     * 
     * we should therefore have aggregate type on the sql table to allow filtering when we get a batch of 
     * records
     * 
     * how do we manage rebuilding a single aggregate, how does that get enqueued?
     * we could have a builder state for each aggregate id ?? which I think was what I started with, 
     * but then how do we quickly get a list of aggregates to be built if we're behind (ie building stopped for some reason )
     * also, how do new aggregates get into this list in the first place?
     * 
     * getFilteredEvents for the aggregateType <<< this could fail when event streams are merged!!!!!!!!!! who knows what the event order will be
     *  -- pulls from global queue
     *  -- updates a single state object
     *  -- updates a record for each aggregate specifying the version that the builder is up to
     *  -- any record with a zero for the version needs a rebuild?
     * 
     * 
     * solution 1)
     *      -- make read models consistent with events, ie in the same transaction.
     * 
     * soluton 2)
     *  - builder queue is added to in same transaction as events
     *  - add the first event version of the aggregate to the workitem
     *  - rebuilds set the version to zero
     * 
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