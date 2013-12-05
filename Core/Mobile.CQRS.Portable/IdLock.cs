//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="IdLock.cs" company="sgmunn">
//    (c) sgmunn 2012  
//
//    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//    documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//    the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//    to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//    The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//    the Software.
//
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//    THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//    CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//    IN THE SOFTWARE.
//  </copyright>
//  --------------------------------------------------------------------------------------------------------------------
//
using System.Collections.Immutable;


namespace Mobile.CQRS
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;


    public class ConcurrentCache<TKey, TValue>
    {
        private IImmutableDictionary<TKey, TValue> _cache = ImmutableDictionary.Create<TKey, TValue>();

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            ////valueFactory.CheckArgumentNull("valueFactory");

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

        public void Clear()
        {
            _cache = _cache.Clear();
        }
    }

//    public sealed class IdLock : IDisposable
//    {
//        private static readonly ConcurrentCache<Guid, object> Locks = new ConcurrentCache<Guid, object>();
//
//        private readonly Guid id;
//        
//        private readonly object lockObject;
//
//        private bool isDisposed;
//
//        public IdLock(Guid id)
//        {
//            this.id = id;
//            this.lockObject = Locks.GetOrAdd(id, x => new object());
//            Monitor.Enter(this.lockObject);
//        }
//
//        public void Dispose()
//        {
//            if (this.isDisposed)
//            {
//                return;
//            }
//
//            this.isDisposed = true;
//
//            object o;
//            Locks.TryRemove(this.id, out o);
//            Monitor.Exit(this.lockObject);
//        }
//    }
}

