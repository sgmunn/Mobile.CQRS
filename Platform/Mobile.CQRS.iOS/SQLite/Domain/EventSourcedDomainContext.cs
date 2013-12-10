// --------------------------------------------------------------------------------------------------------------------
// <copyright file="EventSourcedDomainContext.cs" company="sgmunn">
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

namespace Mobile.CQRS.SQLite.Domain
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Mobile.CQRS.Domain;
    using Mobile.CQRS.Serialization;

    /// <summary>
    /// A domain context set up for typical event sourcing.
    /// You're going to need a db and a way to serialize events at the very least. Optionally you can have a different read model DB.
    /// In order to support sync with a remote event store you will need a command serializer.
    /// In order to support snapshots / momento's you'll need a snapshot serializer.
    /// </summary>
    public sealed class EventSourcedDomainContext : DomainContextBase
    {
        private readonly SQLiteConnection readModelConnection;

        private ReadModelBuilderQueue readModelQueue;

        public EventSourcedDomainContext(string connectionString, ISerializer<IAggregateEvent> eventSerializer) 
        {
            if (connectionString == null)
                throw new ArgumentNullException("connectionString");
            if (eventSerializer == null)
                throw new ArgumentNullException("eventSerializer");

            this.ConnectionString = connectionString;
            this.EventSerializer = eventSerializer;
        }

        public EventSourcedDomainContext(string connectionString, string readModelConnectionString, ISerializer<IAggregateEvent> eventSerializer) 
        {
            if (connectionString == null)
                throw new ArgumentNullException("connectionString");
            if (readModelConnectionString == null)
                throw new ArgumentNullException("readModelConnectionString");
            if (eventSerializer == null)
                throw new ArgumentNullException("eventSerializer");

            this.ConnectionString = connectionString;
            this.EventSerializer = eventSerializer;
            this.ReadModelConnectionString = readModelConnectionString;

            // we need a single read model connection because SQLite gives Busy exceptions if we try to write from 2 connections
            // at the same time - which we do (1 thread enqueues read models and the other updates the read models)
            // if our queue were a different connection then we should not need to do this
            this.readModelConnection = new SQLiteConnection(ReadModelConnectionString, true);
        }

        public string ConnectionString { get; private set; }
        
        public string ReadModelConnectionString { get; private set; }

        public ISerializer<IAggregateEvent> EventSerializer { get; private set; }

        public ISerializer<IAggregateCommand> CommandSerializer { get; set; }
        
        public ISerializer<ISnapshot> SnapshotSerializer { get; set; }

        public IReadModelBuilderAgent BuilderAgent { get; private set; }

        public override IUnitOfWorkScope BeginUnitOfWork()
        {
            var sqlConnectionString = new SQLiteConnectionString(this.ConnectionString, true);
            var connection = SQLiteConnectionPool.Shared.GetConnection(sqlConnectionString);
            var sqlLock = connection.Lock();
            try
            {
                var scope = new SqlUnitOfWorkScope(connection);

                scope.RegisterObject<IEventStore>(new EventStore(connection, this.EventSerializer));

                if (this.CommandSerializer != null)
                {
                    scope.RegisterObject<IPendingCommandRepository>(new PendingCommandRepository(connection, this.CommandSerializer));
                    scope.RegisterObject<IRepository<ISyncState>>(new SyncStateRepository(connection));
                }

                if (this.SnapshotSerializer != null)
                {
                    scope.RegisterObject<ISnapshotRepository>(new SnapshotRepository(connection, this.SnapshotSerializer));
                }

                if (!string.IsNullOrWhiteSpace(this.ReadModelConnectionString))
                {
                    if (this.readModelQueue == null)
                    {
                        this.readModelQueue = new ReadModelBuilderQueue(this.readModelConnection);
                    }

                    scope.RegisterObject<IReadModelQueueProducer>(this.readModelQueue);
                }

                return scope;
            }
            catch
            {
                sqlLock.Dispose();
                throw;
            }
        }

        public override IReadOnlyEventStore GetReadOnlyEventStore()
        {
            var connection = new SQLiteConnection(this.ConnectionString, true);
            return new EventStore(connection, this.EventSerializer);
        }

        public void StartDelayedReadModels()
        {
            if (!string.IsNullOrWhiteSpace(this.ReadModelConnectionString) && this.BuilderAgent == null)
            {
                if (this.readModelQueue == null)
                {
                    this.readModelQueue = new ReadModelBuilderQueue(this.readModelConnection);
                }

                this.BuilderAgent = new ReadModelBuilderAgent(
                    this, 
                    this.Registrations.ToList(), 
                    () => new SqlUnitOfWorkScope(this.readModelConnection), 
                    (scope) => this.readModelQueue);

                this.BuilderAgent.Start();
            }
        }
        
        public async Task SyncSomethingAsync<T>(IEventStore remoteEventStore, Guid aggregateId) where T : class, IAggregateRoot, new()
        {
            var scope = this.BeginUnitOfWork();
            using (scope)
            {
                var syncAgent = new SyncAgent(
                    scope.GetRegisteredObject<IMergableEventStore>(), 
                    remoteEventStore, 
                    scope.GetRegisteredObject<IRepository<ISyncState>>(), 
                    scope.GetRegisteredObject<IPendingCommandRepository>(), 
                    scope.GetRegisteredObject<ISnapshotRepository>());

                if (await syncAgent.SyncWithRemoteAsync<T>(aggregateId).ConfigureAwait(false))
                {
                    await scope.GetRegisteredObject<IReadModelQueueProducer>().EnqueueAsync(aggregateId, AggregateRootBase.GetAggregateTypeDescriptor<T>(), 0).ConfigureAwait(false);

                    if (this.BuilderAgent != null && this.BuilderAgent.IsStarted)
                    {
                        this.BuilderAgent.Trigger();
                    }
                }

                await scope.CommitAsync().ConfigureAwait(false);
            }
        }
    }
}