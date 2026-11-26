# Review characterization checks

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project UnitTests/Review.Characterization
```

This standalone executable links the existing core source without changing its
legacy project or requiring MonoTouch/NUnitLite. It uses the SDK's immutable
collections implementation rather than the old NuGet binary. Assembly metadata
is excluded because its wildcard version conflicts with deterministic builds.

All eight checks assert **observed bugs**, and exit zero when all are reproduced.
They are evidence for the review, not a claim that the library is correct. When
fixing a finding, convert its check to assert the desired contract and move it
into the normal test suite. Run with `dotnet run`, not `dotnet test`.

The actual `SqlUnitOfWorkScope` source is included, but its connection is a small
control-flow double. This verifies transaction/monitor ownership on a participant
failure, not actual SQLite commits, locking, recovery, or mobile execution. The
merge adapter exposes the existing in-memory store's merge method through
`IMergableEventStore`; the production class declares only `IEventStore`.

No HTTP endpoint is contacted. No UI behavior was changed. The historical iOS
suite and real SQLite integration were not executed.
