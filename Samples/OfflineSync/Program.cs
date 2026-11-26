using Mobile.CQRS;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Reactive;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.SQLite.Domain;

namespace Mobile.CQRS.Samples.OfflineSync;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--directory"))
        {
            Console.Error.WriteLine("Usage: dotnet run --project Samples/OfflineSync [-- --directory EMPTY_DIRECTORY]");
            return 2;
        }
        var temporary = args.Length == 0;
        var directory = temporary ? Path.Combine(Path.GetTempPath(), "cqrs-demo-" + Guid.NewGuid()) : Path.GetFullPath(args[1]);
        var created = false;
        try
        {
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidOperationException("Choose an empty directory; existing data will not be overwritten.");
            Directory.CreateDirectory(directory);
            created = true;
            await Demo.RunAsync(directory, Console.Out);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (temporary && created) Directory.Delete(directory, recursive: true); }
    }
}

public sealed record RecoveryScenario(Guid AggregateId, int InitialDelta, int LocalDelta, int OtherDelta,
    int ExpectedQuantity, int ExpectedVersion);

public static class Demo
{
    public static async Task RunAsync(string directory, TextWriter output, RecoveryScenario scenario = null)
    {
        scenario ??= new RecoveryScenario(Guid.Parse("11111111-1111-1111-1111-111111111111"), 10, 2, 3, 15, 3);
        var codec = new StockEventSerializer();
        var clientPath = Path.Combine(directory, "client-a.db");
        var modelsPath = Path.Combine(directory, "read-models.db");
        await using var server = Context(Path.Combine(directory, "canonical.db"));
        await using var client = Context(clientPath, modelsPath);
        await using var other = Context(Path.Combine(directory, "client-b.db"));
        var endpoint = server.AsSyncRemote("sample-stock-canonical-v1");
        var id = scenario.AggregateId;
        client.Execute<StockAggregate>(new AdjustStock { AggregateId = id, Delta = scenario.InitialDelta });
        await client.SyncWithRemoteAsync<StockAggregate>(endpoint, id);
        await other.SyncWithRemoteAsync<StockAggregate>(endpoint, id);
        await output.WriteLineAsync("Created stock offline and downloaded it on a second client.");

        client.Execute<StockAggregate>(new AdjustStock { AggregateId = id, Delta = scenario.LocalDelta });
        other.Execute<StockAggregate>(new AdjustStock { AggregateId = id, Delta = scenario.OtherDelta });
        await other.SyncWithRemoteAsync<StockAggregate>(endpoint, id);
        try
        {
            await client.SyncWithRemoteAsync<StockAggregate>(endpoint, id);
            throw new Exception("Expected an explicit conflict policy to be required.");
        }
        catch (ConflictException) { await output.WriteLineAsync("Concurrent offline edits conflict safely; pending work is preserved."); }

        var lossy = new LostReplyRemote(endpoint);
        try
        {
            await client.SyncWithRemoteAsync<StockAggregate>(lossy, id, new AdditiveStockPolicy());
            throw new Exception("Expected a simulated lost response.");
        }
        catch (TimeoutException) { await output.WriteLineAsync("Remote accepted the replay, but its response was lost."); }
        Guid operation;
        using (var scope = client.BeginUnitOfWork())
        {
            operation = scope.GetRegisteredObject<ISyncOperationRepository>().Get(id).Batch.OperationId;
            var branch = (StockSnapshot)scope.GetRegisteredObject<ISnapshotRepository>().GetById(id);
            Require(branch != null && branch.Quantity == scenario.InitialDelta + scenario.LocalDelta && branch.Version == 2,
                "Expected the speculative branch snapshot before recovery.");
        }

        await using var reopened = Context(clientPath, modelsPath);
        var recovered = await reopened.SyncWithRemoteAsync<StockAggregate>(endpoint, id);
        Require(recovered.OperationId == operation, "Recovery changed operation identity.");
        using (var scope = reopened.BeginUnitOfWork())
        {
            Require(scope.GetRegisteredObject<ISnapshotRepository>().GetById(id) == null, "Branch snapshot was not invalidated.");
            Require(scope.GetRegisteredObject<IPendingCommandRepository>().PendingCommandsForAggregate(id).Count == 0, "Commands were not acknowledged.");
            var aggregate = (StockAggregate)new AggregateRepository(() => new StockAggregate(),
                scope.GetRegisteredObject<IEventStore>(), scope.GetRegisteredObject<ISnapshotRepository>()).GetById(id);
            Require(aggregate.Quantity == scenario.ExpectedQuantity && aggregate.Version == scenario.ExpectedVersion, "Recovered state is incorrect.");
        }
        await other.SyncWithRemoteAsync<StockAggregate>(endpoint, id);
        var canonical = (await endpoint.ReadAsync(id)).Events;
        using (var scope = reopened.BeginUnitOfWork())
            Require(SyncProtocol.Encode(scope.GetRegisteredObject<IEventStore>().GetAllEvents(id), codec).SequenceEqual(canonical), "Client A diverged.");
        using (var scope = other.BeginUnitOfWork())
            Require(SyncProtocol.Encode(scope.GetRegisteredObject<IEventStore>().GetAllEvents(id), codec).SequenceEqual(canonical), "Client B diverged.");
        await output.WriteLineAsync("Reopened client recovered the same frozen operation and invalidated its snapshot.");

        await Project(reopened, modelsPath, id, scenario);
        using (var scope = reopened.BeginReadModelUnitOfWork())
        {
            scope.GetRegisteredObject<SQLiteConnection>().Execute("update StockProjection set Quantity = 999");
            scope.Commit();
        }
        reopened.RebuildReadModels<StockAggregate>(id);
        await Project(reopened, modelsPath, id, scenario);
        await output.WriteLineAsync("Rebuilt a deliberately corrupted read model from committed history.");
        await output.WriteLineAsync($"PASS: both clients, canonical history and projection converge to quantity {scenario.ExpectedQuantity} at version {scenario.ExpectedVersion}.");
    }

    public static EventSourcedDomainContext Context(string path, string modelsPath = null)
    {
        var context = modelsPath == null ? new EventSourcedDomainContext(path, new StockEventSerializer())
            : new EventSourcedDomainContext(path, modelsPath, new StockEventSerializer());
        context.CommandSerializer = new CommandCodec();
        context.SnapshotSerializer = new SnapshotCodec();
        var registration = AggregateRegistration.ForType<StockAggregate>();
        if (modelsPath != null)
            registration = registration.WithDelayedReadModel(scope => {
                var connection = scope.GetRegisteredObject<SQLiteConnection>();
                connection.CreateTable<StockProjection>();
                return new StockProjector(new SqlRepository<StockProjection>(connection, "Identity"));
            });
        context.Register(registration);
        return context;
    }

    private static async Task Project(EventSourcedDomainContext context, string modelsPath, Guid id, RecoveryScenario scenario)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = context.EventBus.Subscribe(notification => {
            if (notification.Event is IReadModelBuilderFaulted fault) done.TrySetException(fault.Fault);
            if (notification.Event is IModelChangeEvent)
            {
                using var connection = new SQLiteConnection(modelsPath);
                var row = connection.Find<StockProjection>(id);
                if (row != null && row.Quantity == scenario.ExpectedQuantity && row.Version == scenario.ExpectedVersion) done.TrySetResult(true);
            }
        });
        context.StartDelayedReadModels();
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await context.BuilderAgent.StopAsync(); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class CommandCodec : Mobile.CQRS.Serialization.ISerializer<IAggregateCommand>
    {
        private readonly JsonSerializerFor<AdjustStock> codec = new();
        public string SerializeToString(object value) => codec.SerializeToString(value);
        public IAggregateCommand DeserializeFromString(string data) => codec.DeserializeFromString(data);
    }
    private sealed class SnapshotCodec : Mobile.CQRS.Serialization.ISerializer<ISnapshot>
    {
        private readonly JsonSerializerFor<StockSnapshot> codec = new();
        public string SerializeToString(object value) => codec.SerializeToString(value);
        public ISnapshot DeserializeFromString(string data) => codec.DeserializeFromString(data);
    }
    private sealed class LostReplyRemote(ISyncRemote inner) : ISyncRemote
    {
        public string RemoteId => inner.RemoteId;
        public Task<SyncStream> ReadAsync(Guid id, CancellationToken token = default) => inner.ReadAsync(id, token);
        public async Task AppendAsync(SyncBatch batch, CancellationToken token = default)
        {
            await inner.AppendAsync(batch, token);
            throw new TimeoutException("Simulated response loss after durable acceptance.");
        }
    }
}
