// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection(nameof(RedisCacheFixture))]
public abstract class RedisCacheTestBase(RedisCacheFixture fixture) : TestBase
{
    protected RedisCacheFixture Fixture { get; } = fixture;

    protected RedisCache CreateCache(
        string? keyPrefix = null,
        TimeSpan? tagMarkerRefreshWindow = null,
        TimeProvider? timeProvider = null
    )
    {
        var options = new RedisCacheOptions
        {
            ConnectionMultiplexer = Fixture.ConnectionMultiplexer,
            KeyPrefix = keyPrefix ?? "",
        };

        if (tagMarkerRefreshWindow is { } window)
        {
            options.TagMarkerRefreshWindow = window;
        }

        var logger = LoggerFactory.CreateLogger<RedisCache>();
        return new RedisCache(
            new SystemJsonSerializer(),
            timeProvider ?? TimeProvider.System,
            options,
            Fixture.ScriptsLoader,
            logger
        );
    }

    /// <summary>
    /// An app clock for tests that need a later timestamp on a write than on an earlier one. The timestamps the cache
    /// stamps on its entries and invalidation markers come from the injected clock, so advancing it replaces a real
    /// wait. Redis expiry is relative to the Redis server's own clock, so a fixed start date does not shorten any TTL.
    /// </summary>
    protected static FakeTimeProvider CreateFakeClock()
    {
        return new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    protected async Task FlushAsync()
    {
        await Fixture.FlushAllAsync(AbortToken);
    }
}
