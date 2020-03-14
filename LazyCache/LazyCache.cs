using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using System.Threading;
using Microsoft.Extensions.Primitives;

namespace LazyCache
{
    public class LazyCache
    {
        private readonly MemoryCache _cache;
        private readonly TimeSpan defaultExpirationOffset;
        private readonly AsyncDuplicateLock locker = new AsyncDuplicateLock();
        private static CancellationTokenSource _resetCacheToken = new CancellationTokenSource();

        public LazyCache()
        {
            _cache = new MemoryCache(new MemoryCacheOptions());
            defaultExpirationOffset = TimeSpan.FromMinutes(1);
        }



        /// <summary>
        /// Removes the value with the specified key from the mem cache
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>true if the element is successfully found and removed; otherwise, false. This method returns false if key is not found in the cache</returns>
        public void Remove(string key)
        {
            _cache.Remove(key);
        }

        public TCachedItem AddOrGetExisting<TCachedItem>(string key, Func<TCachedItem> valueFactory)
        {
            return AddOrGetExisting(key, defaultExpirationOffset, valueFactory);
        }


        /// <summary>
        /// Get cached item, or if not contained in cache, generate it using the valueFactory and add it to the cache
        /// </summary>
        /// <typeparam name="TCachedItem">Type of item being retrieved/added to cache</typeparam>
        /// <param name="key">A unique identifier for the cache entry to add or get.</param>
        /// <param name="offset">Offset to the date and time at which the cache entry will expire.</param>
        /// <param name="valueFactory">The delegate that is invoked to produce the lazily initialized value when it is needed.</param>
        /// <returns>If a cache entry with the same key exists, the existing cache entry; otherwise, the generated item</returns>
        public TCachedItem AddOrGetExisting<TCachedItem>(string key, TimeSpan expirationOffset, Func<TCachedItem> valueFactory)
        {

            Lazy<TCachedItem> returnedLazyObject;
            using (locker.Lock(key))
            {
                var lazyObject = new Lazy<TCachedItem>(valueFactory);
                returnedLazyObject = _cache.GetOrCreate(key, entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = expirationOffset;
                    entry.AddExpirationToken(new CancellationChangeToken(_resetCacheToken.Token));
                    return lazyObject;
                });
            }
            
            try
            {
                var result = returnedLazyObject.Value;
                if(returnedLazyObject != _cache.GetOrCreate(key, entry => new Lazy<TCachedItem>(valueFactory)))
                {
                    return AddOrGetExisting(key, expirationOffset, valueFactory);
                }
                return result;
            }
            catch
            {
                // Handle cached lazy exception by evicting from cache
                _cache.Remove(key);
                throw;
            }
        }

        /// <summary>
        /// Return from the cache the value for the given key. 
        /// If value is already present in cache, return it else generate value with the given method and return.
        ///
        /// </summary>
        /// <typeparam name="T">Type of the value.</typeparam>
        /// <param name="key">A unique identifier for the cache entry to add or get.</param>
        /// <param name="valueFactory">Function that is run only if a value for the given key is not already present in the cache.</param>
        /// <returns>Returned task-object can be completed or running. Note that the task might result in exception.</returns>
        public Task<T> AddOrGetExistingAsync<T>(string key, Func<Task<T>> valueFactory)
        {
            return AddOrGetExistingAsync(key, defaultExpirationOffset, valueFactory);
        }

        /// <summary>
        /// Return from the cache the value for the given key. 
        /// If value is already present in cache, return it else generate value with the given method and return.
        ///
        /// </summary>
        /// <typeparam name="T">Type of the value.</typeparam>
        /// <param name="key">A unique identifier for the cache entry to add or get.</param>
        /// <param name="expirationOffset">Cache expiration time offset.</param>
        /// <param name="valueFactory">Function that is run only if a value for the given key is not already present in the cache.</param>
        /// <returns>Returned task-object can be completed or running. Note that the task might result in exception.</returns>
        public async Task<T> AddOrGetExistingAsync<T>(string key, TimeSpan expirationOffset, Func<Task<T>> valueFactory)
        {
            
            AsyncLazy<T> asyncLazyValue;
            var releaser = await locker.LockAsync(key);
            try
            {
                asyncLazyValue = _cache.GetOrCreate(key, entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = expirationOffset;
                    entry.AddExpirationToken(new CancellationChangeToken(_resetCacheToken.Token));
                    return new AsyncLazy<T>(valueFactory);
                });
            }
            finally
            {
                releaser.Dispose();
            }
            try
            {
                var result = await asyncLazyValue;

                // The awaited Task has completed. Check that the task still is the same version
                // that the cache returns (i.e. the awaited task has not been invalidated during the await).    
                if (asyncLazyValue != _cache.Get(key))
                {
                    // The awaited value is no more the most recent one.
                    // Get the most recent value with a recursive call.
                    return await AddOrGetExistingAsync(key, expirationOffset, valueFactory);
                }

                return result;
            }
            catch (Exception)
            {
                _cache.Remove(key);
                throw;
            }
        }

        /// <summary>
        /// Reset all cache keys
        /// </summary>
        /// <returns></returns>
        public void ResetCache()
        {
            if (_resetCacheToken != null && !_resetCacheToken.IsCancellationRequested && _resetCacheToken.Token.CanBeCanceled)
            {
                _resetCacheToken.Cancel();
                _resetCacheToken.Dispose();
            }

            _resetCacheToken = new CancellationTokenSource();
        }
    }
}
