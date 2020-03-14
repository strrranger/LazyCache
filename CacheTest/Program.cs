using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CacheTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var cache = new LazyCache.LazyCache();

            int counter = 0;

            Parallel.ForEach(Enumerable.Range(1, 10), i =>
            {
                var item = cache.AddOrGetExistingAsync("test-key", () =>
                {
                    Console.Write(" bump! ");
                    Thread.Sleep(1000);
                    return Task.FromResult(Interlocked.Increment(ref counter));
                });

                Console.Write($"{item.Result} ");
            });
        }
    }
}
