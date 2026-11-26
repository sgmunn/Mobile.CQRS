# Shared behavioral contracts, version 1

These committed JSON fixtures are the compatibility boundary for the first Rust
implementation. The C# NUnit tests in `Integrity/ContractFixtureTests.cs` consume
them without regenerating their expected results. Run:

```sh
dotnet test Mobile.CQRS.sln -c Release --filter FullyQualifiedName~ContractFixtureTests
```

`formatVersion` versions the fixture structure. Event payloads also carry an
independent `eventType` and `schemaVersion`; the stock codec rejects unknown
combinations. This sample format does not replace application serializers or
retroactively migrate existing databases.

## Stream and replay

[`v1/stock-stream.json`](v1/stock-stream.json) describes a stream and a sequence of
operations. Execute each against an empty store in order. `quantity` and `version`
are the expected result of replaying the entire retained stream after **every**
step, including failed operations. Both C# in-memory and SQLite stores run it.

- `append`: exact `expectedVersion`, with events starting at that version + 1.
- `merge`: exact `expectedVersion`; retain the prefix through `afterVersion` and
  replace the rest with the supplied events. SQLite requires a transaction.
- `read-after`: versions strictly greater than `afterVersion`.
- `read-up-to`: versions less than or equal to `upToVersion`.
- `result`: `ok`, `concurrency` (stale head), or `invalid` (malformed history).
  Rejections preserve stored history. Check the expected head before the batch.

Aggregate type and IDs must match; versions start at 1 and are contiguous; event
IDs are unique in retained histories. A removed speculative tail can be replaced
with events using its IDs, but a retained prefix or another stream cannot share
those IDs. An empty append still checks concurrency. The stock aggregate sums
signed 32-bit deltas with checked arithmetic; these vectors use positive deltas.

UUIDs use lowercase hyphenated strings. Event timestamps are UTC RFC 3339 strings
with seven fractional digits in the sample codec. The stream fixture's whitespace
is insignificant: its event objects are decoded before storage. Expected states
were authored independently of replay output.

## Frozen sync receipts

[`v1/sync-receipt.json`](v1/sync-receipt.json) freezes a C# reference batch and its
SHA-256 fingerprint. Decode the outer JSON string `canonicalBatchJson`, then hash
its exact UTF-8 bytes, with no BOM or trailing newline. Compare the uppercase
hexadecimal digest with `sha256`. Also decode the batch and verify its embedded
stock payload, accept it twice with only one receipt, and reject reuse of its
operation ID with different payload bytes. The C# test checks all these steps
against the SQLite endpoint.

The receipt algorithm is SHA-256 over the existing `System.Text.Json` serialization
of `SyncBatch`, **not** a generic canonical-JSON algorithm. Its properties are
ordered `OperationId`, `AggregateId`, `AggregateType`, `ExpectedVersion`, `Events`;
event properties are `Identity`, `Version`, `Data`. `Data` is a frozen serialized
string, not an object to normalize. Escaping, property order and whitespace affect
the fingerprint. A Rust implementation interoperating with existing receipts must
match these bytes, including the reference encoder's string escaping. Changing
this algorithm requires an explicitly versioned protocol and migration plan.

The outer fixture file can be pretty-printed without changing the decoded string.
Do not regenerate the golden string/hash as part of tests; a mismatch requires a
compatibility decision. This vector covers the sample envelope, not every possible
application string; extend vectors when adding supported wire representations.

## Offline recovery scenario

[`v1/offline-recovery.json`](v1/offline-recovery.json) supplies the inputs and
expected result to the public offline sample:

1. Client A creates stock; sync to canonical and client B.
2. Both edit offline; B syncs first. A conflicts without policy and retains work.
3. Approve additive replay, accept A's frozen batch remotely, then lose its reply.
4. Reopen A, retry the same operation ID, acknowledge only its prepared commands,
   invalidate its old snapshot and compare both clients with canonical history.
5. Project committed history, corrupt the derived quantity, request a durable
   rebuild, and verify the final quantity/version.

The C# runner uses real SQLite files and an in-process transport wrapper. The
existing transaction, projection and sync suites additionally test forced process
exit recovery; the JSON scenario alone is not that crash-test suite. A Rust runner
should reproduce each assertion rather than only checking the final quantity.
