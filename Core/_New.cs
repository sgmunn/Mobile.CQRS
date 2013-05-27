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
    // TODO: unit of work scope, is this correct, should we have to pass it in or what?

    // TODO: read model builders need to be queue / pointer based

    // TODO: async SQLite

    // TODO: test coverage

    // TODO: seal classes where needed

    // TODO: get read model builders to take a list of events instead of one at a time

    // TODO: update snapshot after sync

    // TODO: can we separate out read / writes from repositories

    // TODO: that might be all for now

    // just a note, concurrentqueue blockingcollection for events ??


/*
 * unit of work has commit, thus if anything supports rollback it should therefore implement IUnitOfWork
 *  - SqlRepository participate in a unit of work scope without having to know about the scope
 * 
 *  - a SQL Server repository where we pass in the transaction will participate in _that_ scope explicitly
 *    it could participate in it's own transaction if it so wanted to
 * 
 * if we had the second type of repo, then we must have the scope before we can get the repository
 *  and the repository will be added to the scope 
 * I think that means that we need to be able to get a repo etc from a scope, or at least resolved from a scope
 * 
 * IDomainUnitOfWorkScope
 *   -- event store
 *   -- sync state
 *   -- aggregate repo
 *   -- pending commands
 *   -- manifest
 *   -- repository
 *   -- probably even a command executor
 * 
 * this is similar to our IResolvingUoW at work.
 * 
 * 
 *
 */

    // how about this, get the domain uow scope, now you can interact with data in a transaction scope
    // that means that we need to support in memory event store uow
    public interface IDomainUnitOfWorkScope : IUnitOfWorkScope
    {
        // these are gets to infer that you are getting a new one, not a property which implies that it hangs around??
        IEventStore EventStore { get; }
        IPendingCommandRepository PendingCommands { get; }
        IRepository<ISyncState> SyncState { get; }
        // etc
        // TODO: we would need a way to register different repository instances
        // TODO: we need to register the scope field name for rebuilding read models
        IRepository<T> GetRepository<T>() where T : IUniqueId, new();
    }

    public class InMemoryDomainScope : InMemoryUnitOfWorkScope, IDomainUnitOfWorkScope
    {
        private readonly IEventStore eventStore;

        public InMemoryDomainScope() 
        {
            this.eventStore = new InMemoryEventStore();
        }

        public IEventStore EventStore 
        {
            get
            {
                return this.eventStore;
            }
        }

        public IPendingCommandRepository PendingCommands { get; private set; }

        public IRepository<ISyncState> SyncState { get; private set; }

        public IRepository<T> GetRepository<T>() where T : IUniqueId, new()
        {
            throw new NotSupportedException();
           // return new D<T>(this.Connection);
        }
    }

    public class SqlDomainScope : SqlUnitOfWorkScope, IDomainUnitOfWorkScope
    {
        public SqlDomainScope(SQLiteConnection connection, ISerializer<IAggregateEvent> eventSerializer, ISerializer<IAggregateCommand> commandSerializer) 
            : base(connection)
        {
            if (eventSerializer == null)
                throw new ArgumentNullException("eventSerializer");

            this.EventStore = new EventStore(connection, eventSerializer);

            if (commandSerializer != null)
            {
                this.PendingCommands = new PendingCommandRepository(connection, commandSerializer);
                this.SyncState = new SyncStateRepository(connection);
            }
        }

        public IEventStore EventStore { get; private set; } 

        public IPendingCommandRepository PendingCommands { get; private set; }
        
        public IRepository<ISyncState> SyncState { get; private set; }

        public IRepository<T> GetRepository<T>() where T : IUniqueId, new()
        {
            // TODO: we need to register the scope field name for rebuilding read models
            return new SqlRepository<T>(this.Connection);
        }
    }

    public class Test
    {

        public void X()
        {
            var es = new EventStore(null, null);

            // if we passed in a transaction, then it would implicitly be part of the scope
            // or do we 



        }
    }






}