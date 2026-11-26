using System;
using System.Collections.Concurrent;

namespace Mobile.CQRS;

public sealed class ConcurrentCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> cache = new();
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) => cache.GetOrAdd(key, valueFactory);
    public bool TryGetValue(TKey key, out TValue value) => cache.TryGetValue(key, out value);
    public void TryAdd(TKey key, TValue value) => cache.TryAdd(key, value);
    public bool TryRemove(TKey key, out TValue value) => cache.TryRemove(key, out value);
    public void Clear() => cache.Clear();
}
