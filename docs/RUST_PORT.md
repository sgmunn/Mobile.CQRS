# Rust port starting point

The C# implementation is the executable reference. Its maintained solution builds
on .NET 10 without mobile workloads, and CI runs on macOS and Linux. The original
review reproductions have been retired in favor of correctness tests. The next
implementation step can be a Rust prototype; no Rust library or mobile binding is
included in this change.

## Groundwork now available

- Stored event inputs and read results cannot mutate in-memory history. Both
  stores validate IDs, type, contiguous versions and concurrency before mutation;
  SQLite adds a unique stream-position index and atomic append batches.
- Replay validates all envelope metadata before calling handlers. Snapshots are
  optional derived state for event-sourced aggregates; history replacement
  invalidates them in the same transaction.
- Owned SQLite scopes clean up on failed setup/commit. Event, snapshot, index,
  pending command and outbox writes share one event-database transaction.
- Delayed projections commit their output and checkpoint together, then acknowledge
  the outbox separately. Delivery IDs protect replacement intents; retries are
  bounded, recovery is durable and rebuilds are explicit.
- Sync freezes operations before transport, atomically stores remote receipts and
  retries ambiguous outcomes using the same operation. Acknowledged prefixes are
  immutable; speculative tails require explicit domain policy for replay.
- Cache, keyed lock, service lookup and handler dispatch have targeted regressions.
  The public [offline example](../Samples/OfflineSync) exercises the complete path.
- [Versioned JSON fixtures](../Contracts/README.md) cover stream rejection/replay,
  receipt encoding and the offline recovery scenario. Their C# runners provide a
  starting interoperability oracle.

Run the reference baseline before comparing a port:

```sh
dotnet restore Mobile.CQRS.sln
dotnet build Mobile.CQRS.sln -c Release --no-restore
dotnet test Mobile.CQRS.sln -c Release --no-build --no-restore
dotnet run --project Samples/OfflineSync -c Release --no-build --no-restore
```

## First Rust milestone

Start a small workspace with a domain/core crate and a SQLite adapter crate. Keep
the C# implementation intact as the reference. Use explicit typed commands/events
and handler traits rather than reproducing reflection, service lookup or the C#
class hierarchy. Separate event-envelope metadata from versioned payload bytes;
own those bytes so no caller can mutate persisted history through a shared value.

Implement in this order:

1. Stock aggregate, envelope validation, pure replay and an in-memory event store.
   Consume `stock-stream.json` directly and compare every step. Cover malformed
   replay, duplicate IDs across streams, input/read isolation and checked overflow
   with Rust tests in addition to the shared vectors.
2. SQLite event store and explicit transaction ownership. Run the same fixture,
   then inject failed writes and verify atomic events/index/snapshot/outbox state
   on reopening. Keep exact expected-version checks, including empty appends.
3. Durable projection outbox and grouped checkpoint/receipt processing. Prove
   rollback, redelivery after target commit, superseded intents, bounded retries,
   shutdown/restart and explicit rebuilds. Acknowledge only the delivery processed.
4. Frozen sync journal, durable remote receipt and ordered policy-approved rebase.
   Match `sync-receipt.json` exactly, then reproduce `offline-recovery.json` and
   the process-exit failure points in the C# recovery tests. Do no network I/O
   inside SQLite transactions.

For the first usable prototype, require all shared fixtures to pass on both Rust
stores, equivalent crash/recovery tests, and the complete console scenario on
macOS/Linux. Add Rust CI before calling the prototype complete. Do not declare
interoperability solely from final aggregate values: compare event IDs, positions,
types, frozen payloads and operation/receipt identities at the relevant boundaries.

Preserve semantics, but design idiomatic Rust APIs. In particular, represent
unchecked command preconditions explicitly: legacy C# command execution treats
its optional expected version 0 as unchecked, while the event store treats 0 as
an empty stream. Avoid encoding both meanings in the same Rust integer API.
Snapshot loading failures should create a fresh replay instance. A failed handler
can have partially changed memory; discard that aggregate instead of promising
automatic in-memory rollback.

## Compatibility decisions before expansion

The initial port can use separate SQLite files and an in-process remote. Reusing
existing C# database files is a separate migration promise: table names, UUID/text
representation, indexes, command codecs, timestamps, receipt encoding and existing
data need explicit cross-runtime tests before making it. Matching the checked-in
sample wire vector is a narrower claim than supporting every application codec.
Keep frozen payload bytes stable; use schema versions/upcasters for evolution.

Production HTTP/authentication, FFI/mobile bindings, package publishing, deployment,
large-stream paging and performance tuning follow the prototype. They are not
prerequisites to start it. The C# reference still loads full histories to validate
reads, uses synchronous thread-affine transactions/locks, and groups all delayed
builders under one checkpoint per aggregate. Ports can improve these choices once
tests prove equivalent durability and concurrency behavior. External effects from
handlers remain outside database transactions; no exactly-once network guarantee
is implied.
