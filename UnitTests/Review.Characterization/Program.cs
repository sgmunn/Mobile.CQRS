using Mobile.CQRS;
using Mobile.CQRS.Domain;

// These checks assert OBSERVED DEFECTS, not the intended contract. A repair should
// replace the corresponding check with a regression test for correct behavior.
var failures = 0;
Check("registered concrete store cannot resolve its interface", () => {
    var scope = new InMemoryUnitOfWorkScope();
    var store = new MergeStore();
    scope.RegisterObject<MergeStore>(store);
    Require(scope.GetRegisteredObject<IEventStore>() == null);
});
Check("cache removal also removes unrelated keys", () => {
    var cache = new ConcurrentCache<int, string>();
    cache.TryAdd(1, "a"); cache.TryAdd(2, "b");
    cache.TryRemove(1, out _);
    Require(!cache.TryGetValue(2, out _));
});
Check("stored event can be mutated by caller", () => {
    var store = new InMemoryEventStore();
    var id = Guid.NewGuid();
    var evt = new Event { AggregateId = id, Version = 1 };
    store.SaveEvents(id, new IAggregateEvent[] { evt }, 0);
    evt.Version = 90;
    Require(store.GetCurrentVersion(id) == 90);
});
Check("replay accepts wrong aggregate and skipped versions", () => {
    var aggregate = new Aggregate { Identity = Guid.NewGuid() };
    aggregate.LoadFromEvents(new IAggregateEvent[] {
        new Event { AggregateId = Guid.NewGuid(), Version = 7 }
    });
    Require(aggregate.Version == 7);
});
Console.WriteLine($"{4 - failures}/4 defect reproductions confirmed.");
return failures == 0 ? 0 : 1;

void Check(string name, Action test) {
    try { test(); Console.WriteLine($"CONFIRMED: {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"UNCONFIRMED: {name}: {ex}"); }
}
static void Require(bool condition) { if (!condition) throw new Exception("Observation changed"); }

sealed class Event : IAggregateEvent {
    public Guid Identity { get; set; } = Guid.NewGuid();
    public Guid AggregateId { get; set; }
    public string AggregateType { get; set; } = "review";
    public int Version { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid CommandId { get; set; }
    public bool ConflictsWith(IAggregateEvent other) => false;
}
sealed class Aggregate : AggregateRootBase {
    private void Apply(Event evt) { }
}
// Adapter exposes the existing in-memory merge method for the lookup reproduction.
sealed class MergeStore : IMergableEventStore {
    private readonly InMemoryEventStore store = new();
    public void Dispose() => store.Dispose();
    public void SaveEvents(Guid id, IList<IAggregateEvent> events, int version) => store.SaveEvents(id, events, version);
    public void MergeEvents(Guid id, IList<IAggregateEvent> events, int version, int from) => store.MergeEvents(id, events, version, from);
    public int GetCurrentVersion(Guid id) => store.GetCurrentVersion(id);
    public IList<IAggregateEvent> GetAllEvents(Guid id) => store.GetAllEvents(id);
    public IList<IAggregateEvent> GetEventsAfterVersion(Guid id, int version) => store.GetEventsAfterVersion(id, version);
    public IList<IAggregateEvent> GetEventsUpToVersion(Guid id, int version) => store.GetEventsUpToVersion(id, version);
}
