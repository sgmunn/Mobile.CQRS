# Mobile.CQRS review and renewal proposal

Reviewed 2026-11-26 against commit `b6ae827`. This is a review and proposed design;
production library behavior has not been changed.

Update: the subsequent .NET 10 baseline replaces the legacy solution build and
addresses finding 1 (scope lifecycle, ownership, failed BEGIN/COMMIT tracking).
Transaction reproductions are now desired-behavior NUnit tests using real SQLite;
six other defect reproductions remain. The findings and build results below are
historical review evidence. See the root README for the current build commands
and supported transaction contract.

The architecture is worth preserving: aggregates generate events, persistence is
abstracted, snapshots are optional, and immediate and delayed projections are
separate concerns. Offline operation is the most distinctive direction to develop.
The first priority is reliable transaction and synchronization semantics. A new
language will not supply those semantics automatically.

## Scope and evidence

Reviewed the core aggregate/command/repository pipeline, units of work,
serialization and reflection, reactive helpers, SQLite repositories and queue,
sync implementation, HTTP extension, projects, samples and existing test coverage.
This is not a line-by-line audit of the vendored SQLite implementation or a
security audit of a remote service.

`dotnet build Mobile.CQRS.sln --no-restore` fails with seven errors on the installed
.NET 10.0.201 SDK. The solution references the absent
`Extensions/Mobile.CQRS.Extensions.iOS.csproj`; other failures involve missing
Portable v4.5, MonoDroid and .NET Framework reference targets. The existing tests
depend on MonoTouch/NUnitLite, so they were not run.

The new [characterization harness](../UnitTests/Review.Characterization/README.md)
compiles the existing core source on .NET 10 and confirms eight defect scenarios.
One pre-existing unused-variable warning remains. Findings below distinguish
executable evidence from source analysis. No real SQLite crash test, iOS simulator
test, network integration test, benchmark or Rust implementation was performed.

## Findings, in priority order

### 1. P1: a failed commit can strand a transaction and its monitor

[InMemoryUnitOfWorkScope.cs](../Core/Mobile.CQRS.Portable/InMemory/InMemoryUnitOfWorkScope.cs#L72)
sets `committed = true` before committing participants or calling `AfterCommit`.
If a participant throws, `Dispose` skips `FinallyDispose`, the path that rolls back
in [SqlUnitOfWorkScope.cs](../Platform/Mobile.CQRS.iOS/SQLite/SqlUnitOfWorkScope.cs#L58).
A command can fail while leaving a transaction open and the connection monitor
held. The harness confirms this using the real scope with a connection double.

Uncommitted scopes also dispose every participant twice (confirmed). Committed
scopes never mark themselves disposed. If `BeginTransaction` throws after
`Monitor.Enter`, construction has no cleanup path. Connection lifetime is not
explicit: contexts allocate connections per scope, while the scope does not close
them. Nested scopes silently share an existing transaction without a defined
savepoint/abort contract.

Proposed fix: a small explicit lifecycle with active/committed/failed/disposed
states; mark committed only after durable success; always release owned resources
exactly once; reject unsupported nesting. Add failure injection at participant,
database commit and rollback boundaries. Specify connection ownership separately
from transaction ownership.

### 2. P1: projection work is not atomic with event persistence

[EventSourcedDomainContext.cs](../Platform/Mobile.CQRS.iOS/SQLite/Domain/EventSourcedDomainContext.cs#L100)
registers a queue using a second read-model connection, while commands commit on
the event-store connection. Work can become visible before events commit, or
survive a command rollback. A worker can read old history and dequeue the only
notification for a newly committed event. `SyncSomething` also triggers the worker
before its local scope commits. This is source analysis, not a reproduced database
race.

Proposed fix: write an outbox/projection intent alongside events in the **same
database transaction**. Consume it after commit. When read models are in a separate
database, commit projection output and its checkpoint together there, then
acknowledge delivery. Tolerate redelivery and make rebuilding possible. Do not
claim exactly-once delivery across independent commits. SQLite documents the
conditions for [atomic multi-database transactions](https://www.sqlite.org/lang_attach.html);
using separate connections is not such a transaction.

### 3. P1: first sync can construct an invalid stream

[SyncAgent.cs](../Core/Mobile.CQRS.Portable/Domain/SyncAgent.cs#L129) treats no sync
state as proof of local origin and returns local events unchanged. If two devices
have independent version-1 events for the same aggregate, both streams can end up
with versions `[1, 1]`. The harness confirms this with the existing in-memory
implementation. The SQLite schema likewise has no unique `(AggregateId, Version)`
constraint; it would not provide this defense by itself.

Proposed fix: distinguish unknown ancestry, local creation and first download.
Reject unknown divergent ancestry unless an explicit domain policy resolves it.
Validate stream ID, contiguous versions, batch identity and expected version on
every append. Enforce unique stream/version and event identity at the database
boundary. Keep published history append-only; isolate speculative local history
from canonical history instead of deleting canonical events during reconciliation.

### 4. P1: sync does not reliably refresh derived state

[SyncAgent.cs](../Core/Mobile.CQRS.Portable/Domain/SyncAgent.cs#L105) requests a
projection rebuild only when `LastSyncedVersion > 0`. Downloading an aggregate for
the first time imports events but returns false, so `SyncSomething` enqueues no
projection work. Confirmed by the harness.

Snapshots are another source-analysis risk: receiving remote changes with no
pending commands returns before rebuilding or invalidating the old snapshot.
After local speculative history is replaced, a snapshot at the same version can
still represent the discarded branch. `AggregateRepository` trusts its version
and skips those canonical events.

Proposed fix: return a structured sync result with changed streams and rebuild
positions. Invalidate branch-dependent snapshots transactionally, and enqueue
projection work for every imported change, including the first import.

### 5. P1: sync has no recovery protocol for ambiguous remote success

[SyncAgent.cs](../Core/Mobile.CQRS.Portable/Domain/SyncAgent.cs#L100) removes pending
commands, merges locally and advances local sync state before calling the remote
append. An outer SQLite transaction can roll back local changes on an ordinary
failure; it cannot roll back an accepted remote write. Losing its response or
crashing before local commit makes the next attempt ambiguous. Re-executing
commands generates new event IDs/timestamps and can duplicate effects. Pairwise
`ConflictsWith` checks do not solve transport retries or prove domain invariants.

Proposed fix: durable stable operation IDs, remote deduplication with a stored
result, explicit acknowledgement state and retryable reconciliation. Keep network
requests outside long-held database write transactions. Test response loss after
acceptance, restart at every transition, duplicate commands and concurrent remote
writes. Define which commands may be rebased: commands using time, randomness or
external state need explicit inputs or a domain-specific policy.

### 6. P1: per-ID locking can stop excluding concurrent callers

[ConcurrentCache.cs](../Core/Mobile.CQRS.Portable/ConcurrentCache.cs#L57) discards
the dictionary returned by immutable `Add` while removing a key. Removing one
entry empties the cache, including unrelated locks (confirmed by the harness).
[IdLock.cs](../Core/Mobile.CQRS.Portable/IdLock.cs#L43) also removes a lock while
other callers can already hold references to it. Even fixing the dictionary leaves
a waiter on the old monitor while a newcomer acquires a new one for the same ID.
This second race is source analysis; `IdLock` is not currently called by the core
command path, which limits its present impact.

Proposed fix: remove unused locking infrastructure, or use a bounded striped lock
or carefully reference-counted keyed lock. Database expected-version checks remain
the authority across processes. Replace the custom cache with a standard primitive.

### 7. P1: SQLite read errors are silently reported as missing records

[SqlRepository_T.cs](../Platform/Mobile.CQRS.iOS/SQLite/SqlRepository_T.cs#L64)
catches every exception in `GetById` and returns default. Busy, corruption, schema
and programming errors become indistinguishable from absence; aggregate/snapshot
loading may then take a creation or replay path with false assumptions.

Proposed fix: use a missing-row API or catch only its documented missing-row
exception. Propagate database failures with context. Add busy/corrupt/schema-error
integration tests. This finding is from source inspection.

### 8. P2: event and snapshot boundaries do not protect history

[AggregateRootBase.cs](../Core/Mobile.CQRS.Portable/Domain/AggregateRootBase.cs#L88)
replays events without validating aggregate ID, ordering or contiguous versions.
[InMemoryEventStore.cs](../Core/Mobile.CQRS.Portable/InMemory/InMemoryEventStore.cs#L40)
copies lists but shares mutable event instances; mutating a saved event changes
persisted history. Both behaviors are confirmed. `RaiseEvent` increments version
before invoking the handler, leaving a gap if application fails and the instance
is reused.

Snapshots are saved before events in `AggregateRepository.Save`; standalone
repositories without a common transaction can retain an uncommitted snapshot if
append fails. The generic snapshot loader accepts a wrong snapshot type via `as`,
sets null state and still reports success. The fallback for unsupported snapshots
also assumes an event store exists, although state-only mode explicitly lacks one.

Proposed fix: immutable envelopes, validation at append/replay, explicit snapshot
type/schema validation, and a stated atomic persistence contract. Snapshots should
be disposable acceleration data in event-sourced mode; state-only persistence
needs its own authoritative-state contract.

### 9. P2: background projection scheduling can lose progress

[ReadModelBuilderAgent.cs](../Core/Mobile.CQRS.Portable/Domain/ReadModelBuilderAgent.cs#L98)
drops triggers while a worker owns its flag. A trigger arriving after the last
peek but before flag release can leave durable work waiting indefinitely for a
later trigger. `Trigger` can run before `Start` with a null cancellation source;
`Stop` does not await the worker, and restart replaces shared cancellation state.
Unknown aggregate types stay queued; a full batch of them can spin indefinitely.
Fault state is not an explicit recovery/retry policy. These are source findings.

Proposed fix: one owned worker task, cancellation and awaited shutdown, a wakeup
mechanism that preserves pending signals, durable per-projector checkpoints,
bounded retries and quarantine/reporting of unsupported work. Test lifecycle and
the empty-queue/wakeup race deterministically. Page long histories rather than
materializing every event on every run.

### 10. P2: reflection and service lookup hide API errors

[MethodExecutor.cs](../Core/Mobile.CQRS.Portable/MethodExecutor.cs#L131) chooses the
first declared method with exactly matching parameter types, irrespective of its
name or return type. It can select public static methods, ignores inherited
handlers and does not reject ambiguous matches. Reflection wrappers obscure domain
exceptions and require special care for trimming/AOT. Read models silently ignore
missing handlers, which is useful for unrelated events but risky for a required
projection handler.

[InMemoryUnitOfWorkScope.cs](../Core/Mobile.CQRS.Portable/InMemory/InMemoryUnitOfWorkScope.cs#L48)
tests assignability in the wrong direction for normal interface resolution:
registering a concrete store does not resolve its interface (confirmed). The
current IEventStore registration can resolve IMergableEventStore when its instance
implements that derived interface; this particular production lookup is not the
same failure.

Proposed fix: explicit typed handlers first; retain a validated compatibility
adapter if needed. Resolve services from actual assignable instances with clear
ambiguity/ownership rules, or replace the locator with constructor dependencies.
Use explicit `Any`, `NoStream`, `Exact(version)` expectations instead of making
zero mean both initial version and unchecked command execution.

### 11. P2: HTTP sync is an unready extension

[HttpEventStore.cs](../Extensions/EventStore/HttpEventStore.cs#L102) uses a hardcoded
plain HTTP host, blocks on `.Result`, constructs clients per request, logs response
bodies, and has no configurable authentication, cancellation, timeout/retry policy,
deduplication or structured concurrency errors. The endpoint was not contacted.
The missing extension project also means this source is not evidence of a working
distributed integration.

Proposed fix: injectable long-lived client and HTTPS endpoint, credentials supplied
by the host, async cancellation, bounded requests, safe structured logs and a
versioned protocol with operation IDs. Write protocol contract tests before a new
remote adapter; never retry non-idempotent appends blindly.

## A pragmatic update sequence

1. **Make the specification executable.** Add SDK projects for core and desktop
   tests, initially beside the legacy solution. Target .NET 10 for a C# revival;
   its [LTS support runs to November 2028](https://dotnet.microsoft.com/en-us/platform/support/policy).
   Port existing useful tests and replace performance tests containing only
   `Assert.True(true)` with real measurements outside correctness tests. Turn the
   review reproductions into desired-behavior regression tests as fixes land.
   Add CI, explicit dependency versions, package metadata and a root license
   preserving the existing MIT notices. Retire legacy targets in a major version.
2. **Repair local durability.** Fix lifecycle and error propagation, define one
   append transaction for events/index/outbox, add schema constraints and migrations,
   snapshot invalidation and immutable event boundaries. Exit criterion: failed
   appends leave no events, index changes, snapshots or projection intents behind;
   two concurrent expected-version writers cannot both succeed.
3. **Make projections recoverable.** Add checkpointed replay, resumable rebuilds,
   retry policy, worker ownership and observable lag/errors. Exit criterion: restart
   after each commit boundary produces the same projection as a clean replay,
   including duplicate deliveries and first remote imports.
4. **Redesign sync as a small protocol.** Define canonical versus speculative
   history, acknowledgement and conflict policy. Add a deterministic fake server
   and disk-backed two-device tests. Exit criterion: disconnection, duplicate
   delivery, ambiguous acceptance and restart converge without duplicate effects
   or loss of acknowledged history.
5. **Then improve ergonomics.** Async I/O/cancellation, explicit handler types,
   schema-versioned serialization/upcasters, paged streams, diagnostics and a small
   sample. Keep domain reducers synchronous and deterministic. Benchmark append,
   replay, projection catch-up, memory and startup before optimizing dispatch.

Keep the initial package split small: core contracts, SQLite adapter and optional
sync transport. Avoid introducing a distributed bus, workflow engine, generic
plugin framework or CRDT layer without a concrete consumer.

## Rust direction

I would explore a Rust implementation if the intended product is a shared offline
engine for Swift/Kotlin or Rust applications. If its primary consumers remain C#,
modern .NET is the shorter route to a useful maintained library. Rust's ownership
and typed errors can improve event immutability and resource management, but do
not establish database atomicity, conflict policy or network idempotency.

Prefer a small redesign using shared contract tests over a class-by-class port.
Model an aggregate as `decide(state, command) -> Result<Vec<Event>, DomainError>`
and `apply(state, event) -> state`. Keep persisted envelopes separate from domain
payloads. An envelope should carry event/stream IDs, stream position, stable event
type, schema version, command/operation ID, causation/correlation IDs and timestamp.
Use a wide checked stream position and an explicit expected-version enum.

Start with a runtime-independent domain core plus one synchronous SQLite adapter.
Add async transport at its boundary when needed. Do not hold SQLite locks across
network awaits. Define storage transactions as operations (append + outbox +
checkpoint changes), not as a family of loosely coordinated repositories. Typed
domain enums suit a Rust application; an SDK for arbitrary foreign-language domains
also needs an explicit envelope/callback boundary. Decide where business logic
lives before building bindings: moving only storage into Rust does not automatically
share command validation across mobile hosts.

For native consumers, validate Swift/Kotlin bindings with
[UniFFI](https://mozilla.github.io/uniffi-rs/latest/), which supports those languages
but does not package the library for each platform. For C# interoperability,
evaluate a narrow C ABI with explicit buffer ownership and error values rather
than assuming first-class C# UniFFI support. Rust supports
[static system libraries](https://doc.rust-lang.org/reference/linkage.html) for
non-Rust hosts; [FFI](https://doc.rust-lang.org/nomicon/ffi.html) still requires
careful lifetime, threading and panic-boundary design. Packaging, cancellation,
callbacks and mobile background lifecycle are real work and belong in the spike.

The first Rust milestone should be one aggregate, in-memory and SQLite stores,
append/load/replay, expected-version contention, one checkpointed projection and
reopen/recovery tests. Run the same language-neutral fixtures against C# and Rust.
Only after that add a two-device offline sync scenario and one actual target-host
sample. Gate adoption on correctness, usable bindings, deployment size/startup and
maintenance cost; there is no measured performance case for Rust yet.

## Compatibility and test gates

Preserve sample/real database fixtures before changing the format. Document event
names independently of CLR full names, schema versions, UUID representation and
timestamp precision. A Rust implementation should use a versioned portable format;
do not assume it can deserialize existing DataContract XML automatically. Prefer
a deliberate exporter/importer with backups and integrity checks. Rebuild derived
snapshots/read models rather than treating them as portable authoritative data.

Required tests include stream gap/wrong-ID rejection, immutable stored payloads,
two-writer contention, batch rollback, wrong/unsupported snapshots, replay versus
snapshot equivalence, projection redelivery and restart, unknown queue types,
schema upcasts, and the sync fault matrix above. Use property tests for valid
histories and replay equivalence; use real SQLite for database guarantees and
process termination for crash recovery. Finally exercise the chosen iOS/Android
sample through computer use for launch, command submission, offline edits,
reconnection and restart. The present review changes only documentation and an
executable harness, so there is no changed UI flow to exercise.

Recommended next work: the SDK/test baseline and transaction fixes. A Rust spike
can follow the shared contract suite without committing to a wholesale rewrite.
