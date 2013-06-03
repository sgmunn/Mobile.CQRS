Mobile.CQRS
===========

A CQRS / Event Sourcing framework for MonoTouch and (hopefully) other mobile platforms.

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