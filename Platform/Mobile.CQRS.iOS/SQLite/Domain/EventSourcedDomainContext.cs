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
using System.Linq;

namespace Mobile.CQRS.SQLite.Domain
{
    using System;
    using Mobile.CQRS.Domain;
    using Mobile.CQRS.Serialization;

    /// <summary>
    /// A domain context set up for typical event sourcing.
    /// You're going to need a db and a way to serialize events at the very least. Optionally you can have a different read model DB.
    /// In order to support sync with a remote event store you will need a command serializer.
    /// In order to support snapshots / momento's you'll need a snapshot serializer.
    /// </summary>
    public sealed class EventSourcedDomainContext : DomainContextBase, IAsyncDisposable
    {
        private readonly object builderLifecycle = new object();

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
        }

        public string ConnectionString { get; private set; }

        public string ReadModelConnectionString { get; private set; }

        public ISerializer<IAggregateEvent> EventSerializer { get; private set; }

        public ISerializer<IAggregateCommand> CommandSerializer { get; set; }

        public ISerializer<ISnapshot> SnapshotSerializer { get; set; }

        public IReadModelBuilderAgent BuilderAgent { get; private set; }

        public override IUnitOfWorkScope BeginUnitOfWork()
        {
            var connection = new SQLiteConnection(this.ConnectionString, true);
            var scope = new SqlUnitOfWorkScope(connection, ownsConnection: true);

            try
            {
                var queue = string.IsNullOrWhiteSpace(this.ReadModelConnectionString)
                    ? null : new ReadModelBuilderQueue(connection);
                scope.RegisterObject<IEventStore>(new EventStore(connection, this.EventSerializer, queue));
                if (queue != null) scope.RegisterObject<IReadModelQueueProducer>(queue);

                if (this.CommandSerializer != null)
                {
                    scope.RegisterObject<IPendingCommandRepository>(new PendingCommandRepository(connection, this.CommandSerializer));
                    scope.RegisterObject<IRepository<ISyncState>>(new SyncStateRepository(connection));
                    scope.RegisterObject<ISyncOperationRepository>(new SyncOperationRepository(connection));
                }

                if (this.SnapshotSerializer != null)
                {
                    scope.RegisterObject<ISnapshotRepository>(new SnapshotRepository(connection, this.SnapshotSerializer));
                }

                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        public IUnitOfWorkScope BeginReadModelUnitOfWork()
        {
            if (string.IsNullOrWhiteSpace(this.ReadModelConnectionString))
                throw new InvalidOperationException("A read-model database is required.");
            var connection = new SQLiteConnection(this.ReadModelConnectionString, true);
            var scope = new SqlUnitOfWorkScope(connection, ownsConnection: true);
            try
            {
                scope.RegisterObject<IReadModelCheckpointStore>(new ReadModelCheckpointStore(connection));
                return scope;
            }
            catch { scope.Dispose(); throw; }
        }

        public void StartDelayedReadModels()
        {
            if (string.IsNullOrWhiteSpace(this.ReadModelConnectionString))
                throw new InvalidOperationException("A read-model database is required.");
            lock (this.builderLifecycle)
            {
                this.BuilderAgent ??= new ReadModelBuilderAgent(this, this.Registrations.ToList(),
                    this.BeginReadModelUnitOfWork, scope => new ReadModelBuilderQueue(scope));
                this.BuilderAgent.Start();
            }
        }

        public void RebuildReadModels<T>(Guid aggregateId) where T : IAggregateRoot
        {
            if (!this.Registrations.Any(x => x.AggregateType == typeof(T)))
                throw new InvalidOperationException("Register the aggregate before requesting a rebuild.");
            using (var scope = this.BeginUnitOfWork())
            {
                var queue = scope.GetRegisteredObject<IReadModelQueueProducer>()
                    ?? throw new InvalidOperationException("A read-model database is required.");
                var type = AggregateRootBase.GetAggregateTypeDescriptor<T>();
                var stream = scope.GetRegisteredObject<SQLiteConnection>().Table<AggregateEvent>()
                    .Where(x => x.AggregateId == aggregateId).Take(1).ToList().FirstOrDefault();
                if (stream != null && stream.AggregateType != type)
                    throw new InvalidOperationException("The aggregate ID belongs to a different aggregate type.");
                queue.Enqueue(aggregateId, type, 0);
                scope.Commit();
            }
            this.BuilderAgent?.Trigger();
        }

        public async System.Threading.Tasks.ValueTask DisposeAsync()
        {
            if (this.BuilderAgent != null) await this.BuilderAgent.StopAsync().ConfigureAwait(false);
        }

        public ISyncRemote AsSyncRemote(string remoteId = null)
            => new SQLiteSyncRemote(this.BeginUnitOfWork, this.EventSerializer,
                remoteId ?? "sqlite:" + System.IO.Path.GetFullPath(this.ConnectionString));

        public async System.Threading.Tasks.Task<SyncResult> SyncWithRemoteAsync<T>(ISyncRemote remote, Guid aggregateId,
            ISyncRebasePolicy policy = null, System.Threading.CancellationToken cancellationToken = default)
            where T : class, IAggregateRoot, new()
        {
            var agent = new SyncAgent(this.BeginUnitOfWork, remote, this.EventSerializer);
            var result = await agent.SyncWithRemoteAsync<T>(aggregateId, policy, cancellationToken).ConfigureAwait(false);
            this.BuilderAgent?.NotifyWorkAvailable();
            return result;
        }

        // Compatibility for an already durable append-only store. A scoped SQLite
        // repository cannot acknowledge durability; pass context.AsSyncRemote instead.
        public void SyncSomething<T>(IEventStore remoteEventStore, Guid aggregateId) where T : class, IAggregateRoot, new()
        {
            if (remoteEventStore is EventStore)
                throw new NotSupportedException("Use the remote context's AsSyncRemote endpoint, not an uncommitted SQLite store.");
            var remote = new EventStoreSyncRemote(remoteEventStore, this.EventSerializer,
                "legacy:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(remoteEventStore));
            this.SyncWithRemoteAsync<T>(remote, aggregateId).GetAwaiter().GetResult();
        }
    }
}
