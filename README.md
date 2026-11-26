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

Event, index and snapshot writes using one connection and scope roll back
together. In-memory participants are not reversible transactions. This update
does not make the separate projection database atomic with event persistence,
redesign sync, or fix the remaining review findings. An outbox and recoverable
sync protocol remain follow-up work.
