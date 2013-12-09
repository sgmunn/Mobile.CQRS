/*
 * TODO: async support for remote and local stores.
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
