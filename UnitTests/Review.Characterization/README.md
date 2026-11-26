# Archived review reproductions

The original executable deliberately reproduced eight bugs in the historical
library. It has been retired: all eight scenarios now have desired-behavior NUnit
regressions in [Transactions](../Mobile.CQRS.UnitTests.iOS/Transactions),
[Sync](../Mobile.CQRS.UnitTests.iOS/Sync) and
[Integrity](../Mobile.CQRS.UnitTests.iOS/Integrity). Projection recovery has its own
integration tests as well.

Run `dotnet test Mobile.CQRS.sln -c Release` from the repository root. See the
[historical review](../../docs/REVIEW.md) for original evidence and the root README
for the current behavior and upgrade contract. There is no longer an executable
that succeeds by confirming known bugs.
