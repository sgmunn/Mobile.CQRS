# Review characterization checks

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project UnitTests/Review.Characterization
```

This executable references the .NET 10 core library. All four checks assert
**remaining observed bugs**, and exit zero when all are reproduced. They are
review evidence, not correctness tests. When fixing a finding, replace its check
with a desired-behavior regression test in the NUnit suite. Run this executable
with `dotnet run`, not `dotnet test`.

The two original transaction reproductions have been replaced by NUnit tests
under `Mobile.CQRS.UnitTests.iOS/Transactions`, including real SQLite integration.
The remaining merge adapter exposes the existing in-memory store's merge method
through `IMergableEventStore`; the production class declares only `IEventStore`.
The two former sync reproductions are now desired-behavior tests under
`Mobile.CQRS.UnitTests.iOS/Sync`. No HTTP endpoint is contacted.
