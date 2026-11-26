Mobile.CQRS
===========

A CQRS / event-sourcing library with offline synchronization, snapshots and
recoverable SQLite projections. The maintained baseline targets .NET 10; the
original MonoTouch and other mobile integrations are historical source.

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
dotnet run --project Samples/OfflineSync -c Release --no-build --no-restore
```

The solution includes the core, SQLite adapter, desktop NUnit tests and runnable
offline sync example. Historical `Portable` and `iOS` project/assembly names are retained, but
these projects now target `net10.0`. The old iPhone sample, device test runner,
Android project and HTTP extension remain historical source outside the solution;
they have not been ported to modern mobile frameworks. CI covers macOS and Linux.
Windows requires a compatible native `sqlite3` library and is not yet verified.

NUnit 3 is retained to migrate the existing assertions with minimal churn. The
original defect reproductions have all been replaced by desired-behavior tests.
The [Rust port plan](docs/RUST_PORT.md) and [shared fixtures](Contracts/README.md)
define the starting point for a separate implementation.

## Offline recovery example

[Samples/OfflineSync](Samples/OfflineSync) runs a stock aggregate through two-client
offline edits, a conservative conflict, explicit additive replay, remote acceptance
with a lost response, reopening the client, snapshot invalidation and projection
rebuild. It checks that both clients and the read model converge to quantity 15 at
version 3, then deletes its temporary databases. To inspect the files afterward:

```sh
dotnet run --project Samples/OfflineSync -c Release -- --directory ./demo-data
```

The directory must be absent or empty; supplied directories are retained. The
sample uses public APIs, real SQLite files and an in-process `ISyncRemote` failure
wrapper. It is also exercised by the NUnit contract tests and macOS/Linux CI.

## Event and replay integrity

Set a nonempty aggregate `Identity` before raising or replaying events. A stream
has one stable aggregate type and contiguous versions starting at 1. Event IDs
must be nonempty and unique across retained histories. Both stores validate the
whole batch before changing storage, including an empty append's expected version.
Replay validates the whole batch's identity, type, versions and duplicate IDs
before applying any handlers, and rejects replay over uncommitted changes.
An apply failure does not advance that event's version or enqueue it; handlers
can still have mutated aggregate state, so discard the aggregate after a failure.

Appends check the exact current version: expected version 0 means an absent
stream. This differs from the legacy command executor's optional expected version
0, which skips the caller-level check; its repository still appends against the
version it loaded. `MergeEvents` replaces only the tail after the supplied version
and keeps the aggregate type. It is for speculative local reconciliation, not
rewriting acknowledged history.

`InMemoryEventStore` freezes serialized events on write and returns fresh instances
on reads. `InMemorySnapshotRepository<T>` similarly copies snapshot inputs and
outputs, so mutations cannot change persisted state through a reference. Their
default JSON codec supports public properties/fields; supply `ISerializer<T>` for
custom/polymorphic payloads. Custom codecs must preserve metadata and return fresh
object graphs. SQLite verifies each deserialized event against its stored envelope.
A snapshot with invalid metadata or a version ahead of history falls back to
full replay; invalid authoritative state-only snapshots raise an error.

SQLite adds a unique `(AggregateId, Version)` index. Existing duplicate positions
cause initialization to fail without repairing or deleting history; back up and
investigate corrupt files before retrying. Full stream validation currently loads
and deserializes the aggregate's history even for version/range queries. This
favors correctness for the reference implementation; paging and validated head
metadata can be added with equivalent contract tests later.

Cache removal preserves other keys. `IdLock` uses 128 fixed monitor stripes to
avoid replacing a gate while a caller still owns it; collisions can serialize
unrelated IDs. It is synchronous, reentrant and thread-affine. Scope lookup prefers
an exact registration, otherwise matches actual instances, deduplicates aliases
and rejects ambiguous instances. Reflection dispatch selects a single exact-argument
`void` instance handler on the nearest declaring type, including inherited private
handlers; ambiguous handlers fail explicitly. SQLite repository reads return null
for an absent row and propagate database errors.

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
connection and scope roll back together. A standalone SQLite event append owns
its own transaction; within a scope it uses a savepoint. A native SQLite write
failure can roll back the entire enclosing transaction in the vendored adapter.
A later scope commit detects this and fails; discard that scope after an error. In-memory participants are not reversible
transactions. See the sync contract below for canonical versus speculative history.

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
notifications. Failed work stays queued. Each delivery gets up to three consecutive
attempts, at least one second apart; a persistent fault then pauses the worker.
Ordinary commit notifications wake a healthy worker but do not reset this budget
or resume a paused worker. Inspect `IsFaulted` and `Fault` or subscribe to
`IReadModelBuilderFaulted`, fix the cause, and call `Trigger()` to explicitly retry.
`NotifyWorkAvailable()` is a passive wakeup. Unknown aggregate registrations fault
instead of silently losing work or spinning. One faulty item pauses that worker, so monitor its fault
state. Run one worker per read-model database. External I/O from projection handlers
is not covered by the checkpoint transaction.

Request a durable rebuild with `context.RebuildReadModels<MyAggregate>(id)`. It
transactionally deletes that aggregate's derived rows, replays its history and
replaces its checkpoint. A request for the wrong stored aggregate type is rejected
without replacing pending work. Every delayed builder in a registration forms **one
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
Direct queue enqueue/acknowledgement requires an active transaction. Stores that
atomically enqueue their own work opt into `IEventStore.EnqueuesReadModels`; this
avoids a second enqueue by generic command execution and preserves post-commit
aggregate notifications. The default is false for other adapters.

Recovery tests cover failed command commits, partial projection writes, group
rollback, stale acknowledgements, appends during processing, lost notifications,
bounded retries, cancellation/restart and explicit rebuilds. They also terminate a
separate .NET test host during an uncommitted projection and after projection
commit/before acknowledgement, then reopen the files and recover. The explicit
crash probe is invoked only by those parent tests, not as a standalone normal test.

## Recoverable sync and snapshot invalidation

Use a stable canonical endpoint identity and configure command serialization on
clients. A SQLite peer can expose its own committed endpoint:

```csharp
ISyncRemote endpoint = serverContext.AsSyncRemote("orders-canonical-v1");
SyncResult result = await clientContext.SyncWithRemoteAsync<Order>(endpoint, id,
    cancellationToken: cancellationToken);
```

The event serializers on both peers must be compatible and deterministic. Sync
uses frozen serialized payloads, event IDs and contiguous stream positions;
changing payload bytes, IDs or serializer format within existing history requires
an explicit data migration. `ISyncRemote` is a transport boundary; this change
does not provide a production HTTP server or retrofit the historical HTTP sample.

A client first persists a `SyncOperationRecord` containing the exact outgoing
batch, operation ID, prepared local history, canonical result and command IDs.
It sends that request **outside** its SQLite transactions. `SQLiteSyncRemote`
atomically appends events and stores a `SyncReceipt` fingerprint. Repeating the
same operation succeeds even after later remote appends; reusing its ID with a
different request is rejected. Keep receipts: removing them can break recovery
for clients whose acknowledgement was lost.

After acknowledgement, the client commits history reconciliation, snapshot
invalidation, projection rebuild intent, sync state, removal of only the prepared
commands and journal deletion together. A timeout, cancellation or lost response
leaves the frozen journal and local commands available for retry. Calling sync
again (including after restart) resends that operation, without regenerating its
events. Only a definitive expected-version rejection permits replanning; remote
head races are retried at most three times per call. Remote transport adapters
must check their durable receipt before checking expected version and must never
report `ConcurrencyException` for an ambiguous outcome.

History through `LastSyncedVersion` is an immutable acknowledged prefix; the local
tail remains speculative. Confirmed history is never rebased by sync. First
contact with two different origins for the same aggregate ID is rejected, and
remote truncation or rewriting of an acknowledged prefix is rejected. The client
also persists the endpoint's `RemoteId`; switching endpoints with outstanding or
acknowledged work is rejected. Do not use one endpoint ID for different stores.

Divergent tails fail with `ConflictException` by default. To opt into replay,
provide an `ISyncRebasePolicy` that approves the domain's local and remote changes.
Only then are covered pending commands replayed, in original insertion order,
against canonical history. Commands must be pure/replayable, with time, randomness
and external inputs captured in their data. Pairwise event conflict checks alone
are not a convergence proof. The generated candidate is frozen before sending,
so an unknown outcome never causes another command replay.

Local edits can continue during a request. Their commands are not acknowledged
by that operation. If reconciliation changes their base, replaying those later
commands also requires policy approval. A late conflict leaves the accepted
operation journal and all local changes intact; retry with an appropriate policy
after resolving the domain conflict. There is no automatic discard or force-reset
of speculative data. Run one sync coordinator per local stream; concurrent calls
or SQLite writers may return concurrency/busy errors and can be retried.

`MergeEvents` on SQLite now requires an active transaction and invalidates the
aggregate's snapshot in that transaction, even if snapshot serialization is
currently disabled or the replacement has the same version. Configured stores
also enqueue a full projection rebuild. Unsupported snapshot types fall back to
fresh event replay; state-only persistence raises `UnsupportedSnapshotException`
because it has no event history to recover from. Direct history replacement is a
low-level operation; application code must not use it to rewrite canonical history.

The old `SyncSomething(IEventStore, id)` wrapper remains for already durable,
append-only legacy stores. Its compatibility adapter deduplicates by frozen event
IDs/payloads. It rejects a scoped SQLite repository: use `AsSyncRemote()` so remote
commit finishes before acknowledgement. Its instance-based legacy identity is
not a durable network endpoint identity; new integrations should implement
`ISyncRemote` instead. `SyncAgent` now takes a scope factory, remote and serializer;
custom client stores need transactional `ISyncOperationRepository`, selective
pending-command removal, `ISyncState.RemoteId` and snapshot invalidation support.

Existing SQLite schemas gain journal/receipt tables and remote identity metadata
additively. Back up the event files before upgrading. Legacy sync state is accepted
only when the claimed acknowledged prefix exactly matches both stores; corruption
or unknown ancestry is surfaced instead of resetting state or deleting commands.
The former sync defect reproductions have been replaced by regression tests.
Tests cover two-client imports, conflicts, ordered rebase, remote races, unknown
outcomes, concurrent local edits, cancellation, receipt integrity and invalidation
rollback. Separate test hosts exit before send, after remote acceptance and during
uncommitted local finalization; reopening the files recovers the same operation.
