Mobile.CQRS
===========

A CQRS / Event Sourcing framework for MonoTouch and (hopefully) other mobile platforms.

See the [2026 review and renewal proposal](docs/REVIEW.md) for correctness
findings, a modernization roadmap, and an assessment of a Rust implementation.
The legacy solution requires its historical toolchain; the standalone
[review checks](UnitTests/Review.Characterization/README.md) run on .NET 10.

* Supports Event Sourcing
* Supports "State" Sourced (I made up that term)
* Has support for immediately consistent read models
* Has support for eventually consistent read models (Event Sourced only)
* Has support for syncing with a remote event store
* No dependency on any particular serialisation technology - does provide built-in support for DataContract serializer.
* Built-in support for SQLite.Net, but not dependant.


####Current focus / work items.
* Update SQLite support to use async version
* Better test coverage
* Performance improvements
