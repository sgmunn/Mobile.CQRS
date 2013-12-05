//  --------------------------------------------------------------------------------------------------------------------
//  http://stackoverflow.com/questions/18367839/alternative-to-concurrentdictionary-for-portable-class-library
//  --------------------------------------------------------------------------------------------------------------------

namespace Mobile.CQRS
{
    using System;
    using System.Threading;
    using System.Collections.Immutable;

    public sealed class ConcurrentCache<TKey, TValue>
    {
        private IImmutableDictionary<TKey, TValue> _cache = ImmutableDictionary.Create<TKey, TValue>();

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            TValue newValue = default(TValue);
            bool newValueCreated = false;
            while (true)
            {
                var oldCache = _cache;
                TValue value;
                if (oldCache.TryGetValue(key, out value))
                    return value;

                // Value not found; create it if necessary
                if (!newValueCreated)
                {
                    newValue = valueFactory(key);
                    newValueCreated = true;
                }

                // Add the new value to the cache
                var newCache = oldCache.Add(key, newValue);
                if (Interlocked.CompareExchange(ref _cache, newCache, oldCache) == oldCache)
                {
                    // Cache successfully written
                    return newValue;
                }

                // Failed to write the new cache because another thread
                // already changed it; try again.
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            var oldCache = _cache;
            return oldCache.TryGetValue(key, out value);
        }

        public void TryAdd(TKey key, TValue value)
        {
            this.GetOrAdd(key, x => value);
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            while (true)
            {
                var oldCache = _cache;
                if (oldCache.TryGetValue(key, out value))
                {
                    var newCache = ImmutableDictionary.Create<TKey, TValue>();
                    foreach (var x in oldCache.Keys)
                    {
                        if (!x.Equals(key))
                        {
                            newCache.Add(x, oldCache[x]);
                        }
                    }

                    if (Interlocked.CompareExchange(ref _cache, newCache, oldCache) == oldCache)
                    {
                        // Cache successfully written
                        return true;
                    }

                    // try again
                }
                else
                {
                    return false;
                }
            }
        }

        public void Clear()
        {
            _cache = _cache.Clear();
        }
    }
}

