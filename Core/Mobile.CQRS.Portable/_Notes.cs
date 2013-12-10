/*
 * TODO: async support for remote and local stores.
 * TODO: SyncSomething needs to know how to find aggregates.
 * 
 * I have an idea for handling the locking of the sqlite connections, similar to sqlite-net with its connection pool.
 * 
 * SQLIte Async COnnection
 * connection pool has _one_ connection per database path
 * whenever a connection is requested, that single connection is returned with a lock. If the thread can enter the lock
 * it "owns" the connection. this specific connection is then passed to actions so that they can interact with the db
 * this appears to allow multiple threads to update the DB, but only one instance / process if you will.
 * 
 * 
 * Sooo, when we call BeginUnitOfWork, we get a connection from the pool for the event store.  this will block other threads that
 * are trying to start a unit of work
 * -- read model builder agent starts a unit of work to get the event store (read only)
 * -- sync does the same, but for write as well.
 * 
 * 
 * 
 * 
 * remote event store
 * just an in memory one for now
 * sample to use a fixed root id, or discovery
 * json serialization
 * 
 * 
 * 
 * 
 * 
 * 
 * TODO: Mobile.Mvvm view model with support for loading changes while being editing
 * TODO: last feature is to be able to discover aggregates somehow in order to sync them, and to force rebuild of read models
 * TODO: make tests work again and increse test coverage and in memory support - must handle uow in event store
 * -- in memory snapshot repo needs to use serializer so that when the snapshot is returned we don't mess with the snapshot version
 * TODO: a better way of doing delete where or update where - add query support and then we can loose the scope field in repository
 * TODO: that might be all for now
 */
