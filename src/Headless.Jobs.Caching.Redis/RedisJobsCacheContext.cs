// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Redis-backed cron-expression cache. Node membership/liveness moved to <c>Headless.Coordination</c>;
/// this context is caching-only.
/// </summary>
internal sealed class RedisJobsCacheContext([FromKeyedServices("jobs")] IDistributedCache cache) : IJobsCacheContext
{
    public IDistributedCache DistributedCache => cache;

    public bool HasRedisConnection => true;

    public async Task<TResult[]?> GetOrSetArrayAsync<TResult>(
        string cacheKey,
        Func<CancellationToken, Task<TResult[]?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
        where TResult : class
    {
        try
        {
            var cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);
            if (cachedBytes?.Length > 0)
            {
                ReadOnlySpan<byte> cachedSpan = cachedBytes.AsSpan();
                var cached = JsonSerializer.Deserialize<TResult[]>(cachedSpan);

                if (cached != null)
                {
                    return cached;
                }
            }
        }
#pragma warning disable ERP022, RCS1075
        // ERP022/RCS1075: Cache failures are expected and should not affect business logic.
        // Fall back to factory when cache is unavailable.
        catch (Exception)
        {
            // Cache miss or failure - continue with factory
        }
#pragma warning restore ERP022, RCS1075

        var result = await factory(cancellationToken);

        if (result == null)
        {
            return null;
        }

        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            await using var writer = new Utf8JsonWriter(bufferWriter);

            JsonSerializer.Serialize(writer, result);
            await writer.FlushAsync(cancellationToken);

            await cache.SetAsync(cacheKey, bufferWriter.WrittenMemory.ToArray(), cancellationToken);
        }
        // ERP022/RCS1075: Cache set failures should not affect the result returned to caller.
#pragma warning disable ERP022, RCS1075
        catch (Exception)
        {
            // Cache set failure - result already computed, just can't cache it
        }
#pragma warning restore ERP022, RCS1075

        return result;
    }
}
