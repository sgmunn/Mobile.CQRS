Mobile.CQRS
===========

A CQRS / Event Sourcing framework for MonoTouch and (hopefully) other mobile platforms.

See the [2026 review and renewal proposal](docs/REVIEW.md) for correctness
findings, a modernization roadmap, and an assessment of a Rust implementation.
The main solution now builds and tests on .NET 10.

* Supports Event Sourcing
* Supports "State" Sourced (I made up that term)
* Has support for immediately consistent read models
* Has support for eventually consistent read models (Event Sourced only)
* Has support for syncing with a remote event store
* No dependency on any particular serialisation technology - does provide built-in support for DataContract serializer.
* Built-in support for SQLite.Net, but not dependant.


## Build and test (.NET 10)

Install a .NET 10 SDK and the native SQLite 3 library (`libsqlite3-dev` on Ubuntu;
provided by macOS). No mobile workloads or simulator are needed for this baseline.

```sh
dotnet restore Mobile.CQRS.sln
dotnet build Mobile.CQRS.sln -c Release --no-restore
dotnet test Mobile.CQRS.sln -c Release --no-build --no-restore
dotnet run --project UnitTests/Review.Characterization -c Release --no-build
```

The solution includes the core, SQLite adapter, desktop NUnit tests and review
checks. Historical `Portable` and `iOS` project/assembly names are retained, but
these projects now target `net10.0`. The old iPhone sample, device test runner,
Android project and HTTP extension remain historical source outside the solution;
they have not been ported to modern mobile frameworks. CI covers macOS and Linux.
Windows requires a compatible native `sqlite3` library and is not yet verified.

NUnit 3 is retained to migrate the existing assertions with minimal churn. The
review executable confirms six **remaining defects** outside this update's scope;
its successful exit is not a correctness claim. See its
[README](UnitTests/Review.Characterization/README.md).

## Transaction contract

Use `using` for every scope. A failed commit cannot be retried on the same scope;
disposal rolls back any owned uncommitted SQLite transaction. A successful commit
is idempotent. Disposal is idempotent, cleans each participant once, and attempts
all cleanup even if a participant throws (cleanup errors are aggregated).
`FinallyDispose` now runs after both committed and abandoned scopes.

`SqlUnitOfWorkScope(connection)` borrows the connection. The overload with
`ownsConnection: true` closes it on disposal; domain contexts use this overload
for their per-operation connections. Scopes are synchronous and thread-affine:
create, commit and dispose on the same thread, with no `await` inside. The monitor
is held until disposal. Nested scopes on one connection are rejected; implicit
sharing of an outer transaction is no longer supported.

Event, index, snapshot and configured projection-outbox writes using one
connection and scope roll back together. In-memory participants are not reversible
transactions. Sync conflict resolution remains follow-up work.

## Durable delayed projections

Configure `EventSourcedDomainContext(eventsPath, readModelsPath, eventSerializer)`
and register delayed builders before calling `StartDelayedReadModels()`. Each
builder must use repositories from the supplied read-model scope and implement
`DeleteForAggregate` for rebuilding. Use `await context.DisposeAsync()` (or
`await using`) to stop and join its worker. `Stop()` requests cancellation;
`await context.BuilderAgent.StopAsync()` waits for the active transaction to finish
or roll back. Await shutdown before restarting the same agent.

The `ReadModelWorkItem` table now lives in the **event database**. The context wires
its queue into SQLite event appends and merges, including direct scoped store
writes and first remote imports. Work becomes visible only after the event
transaction commits. Each new intent rotates a delivery ID; acknowledgement of an
older delivery cannot remove a concurrent append or rebuild. The standalone
`EventStore(connection, serializer)` overload remains without an outbox; use the
context for the configured pipeline, or supply a queue on the same transaction.

The worker reads committed history, then commits all delayed builders for an
aggregate together with a `ReadModelCheckpointRecord` in the read-model database.
It acknowledges the source outbox in a subsequent transaction. If it stops between
these commits, a receipt prevents reapplying the delivery. Incremental processing
starts after the checkpoint; a missing checkpoint performs a full rebuild. The
worker never holds a database transaction across an `await` or across both
source and target processing. These are at-least-once deliveries with transactional
projection output, not distributed exactly-once side effects. Notification delivery
is best effort and happens after both commits.

Startup scans durable work, and a one-second poll recovers missed in-process
notifications. Failed work stays queued. There are three attempts per wakeup, at
one-second intervals; a persistent fault then pauses the worker. Inspect `IsFaulted`
and `Fault` or subscribe to `IReadModelBuilderFaulted`, fix the cause, and call
`Trigger()` to retry. Unknown aggregate registrations fault instead of silently
losing work or spinning. One faulty item pauses that worker, so monitor its fault
state. Run one worker per read-model database. External I/O from projection handlers
is not covered by the checkpoint transaction.

Request a durable rebuild with `context.RebuildReadModels<MyAggregate>(id)`. It
transactionally deletes that aggregate's derived rows, replays its history and
replaces its checkpoint. Every delayed builder in a registration forms **one
projection group** with one checkpoint per aggregate. Adding/removing builders or
changing their schema/logic requires rebuilding the affected aggregates. Register
the final builder set before starting the worker; recreate the context to change
registrations. Histories are currently loaded per aggregate in memory; paging and
independent per-projector checkpoints remain future work.

**Upgrading an existing database:** the former queue in the read-model database
is not the authoritative outbox and is not automatically moved. Back up both files,
then enqueue `RebuildReadModels<T>(id)` for all existing event-sourced aggregate IDs
before enabling the worker. This reconstructs rows and checkpoints from committed
history, including work formerly stranded in the old queue. If a read-model file
is lost or intentionally replaced, request rebuilds again; an empty outbox alone
cannot discover aggregates to restore. The rebuild API only changes derived data.

Custom adapters must implement `IReadModelWorkItem.DeliveryId` and provide an
`IReadModelCheckpointStore` in the projection scope with the same commit boundary
as their output. The worker's `queueFactory` now receives an **event** scope for
both reading and acknowledgement, while `scopeFactory` creates projection scopes.
Direct queue enqueue/acknowledgement requires an active transaction.

Recovery tests cover failed command commits, partial projection writes, group
rollback, stale acknowledgements, appends during processing, lost notifications,
bounded retries, cancellation/restart and explicit rebuilds. They also terminate a
separate .NET test host during an uncommitted projection and after projection
commit/before acknowledgement, then reopen the files and recover. The explicit
crash probe is invoked only by those parent tests, not as a standalone normal test.
