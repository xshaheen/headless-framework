// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Tests;

/// <summary>
/// Proves that <c>HeadlessRedisScriptsLoader</c> is isolated per package via keyed DI,
/// so <c>Headless.Caching.Redis</c> and <c>Headless.DistributedLocks.Redis</c> can run against
/// DIFFERENT Redis instances without cross-contamination.
/// </summary>
public sealed class RedisScriptLoaderIsolationTests : TestBase
{
    private readonly RedisContainer _cacheContainer = new RedisBuilder(TestImages.Redis).Build();
    private readonly RedisContainer _lockContainer = new RedisBuilder(TestImages.Redis).Build();

    private ConnectionMultiplexer _cacheMultiplexer = null!;
    private ConnectionMultiplexer _lockMultiplexer = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Task.WhenAll(_cacheContainer.StartAsync(), _lockContainer.StartAsync());

        var cacheConnStr = _cacheContainer.GetConnectionString() + ",allowAdmin=true";
        var lockConnStr = _lockContainer.GetConnectionString() + ",allowAdmin=true";

        _cacheMultiplexer = await ConnectionMultiplexer.ConnectAsync(cacheConnStr);
        _lockMultiplexer = await ConnectionMultiplexer.ConnectAsync(lockConnStr);

        await _cacheMultiplexer.GetDatabase().ExecuteAsync("FLUSHALL");
        await _lockMultiplexer.GetDatabase().ExecuteAsync("FLUSHALL");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_cacheMultiplexer is not null)
        {
            await _cacheMultiplexer.DisposeAsync();
        }

        if (_lockMultiplexer is not null)
        {
            await _lockMultiplexer.DisposeAsync();
        }

        await _cacheContainer.DisposeAsync();
        await _lockContainer.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_isolate_scripts_per_package_when_registered_against_different_multiplexers()
    {
        // --- arrange ---
        var ct = AbortToken;
        var faker = new Faker();

        var cacheKey = $"isolation-test:{faker.Random.AlphaNumeric(12)}";
        var lockResource = $"lock-isolation-test:{faker.Random.AlphaNumeric(12)}";
        var cacheValue = faker.Random.AlphaNumeric(20);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        // locks use the DI-registered IConnectionMultiplexer = lock container (B)
        services.AddSingleton<IConnectionMultiplexer>(_lockMultiplexer);
        services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
        // cache uses its own multiplexer from RedisCacheOptions = cache container (A)
        services.AddHeadlessCaching(setup => setup.UseRedis(o => o.ConnectionMultiplexer = _cacheMultiplexer));

        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ICache>();
        var lockProvider = provider.GetRequiredService<IDistributedLock>();

        // --- act: exercise cache against container A ---
        var upsertResult = await cache.UpsertAsync(cacheKey, cacheValue, TimeSpan.FromMinutes(5), ct);

        // --- act: exercise lock against container B ---
        var lockHandle = await lockProvider.TryAcquireAsync(
            lockResource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            ct
        );

        // --- assert: both operations succeed (no cross-contamination or wrong-multiplexer errors) ---
        upsertResult.Should().BeTrue("cache upsert must succeed against container A");
        lockHandle.Should().NotBeNull("lock acquire must succeed against container B");

        // --- assert: cache key is in container A, NOT in container B ---
        var cacheDbA = _cacheMultiplexer.GetDatabase();
        var cacheDbB = _lockMultiplexer.GetDatabase();

        var keyInA = await cacheDbA.KeyExistsAsync(cacheKey);
        var keyInB = await cacheDbB.KeyExistsAsync(cacheKey);
        keyInA.Should().BeTrue("cache key must exist in container A (the cache Redis instance)");
        keyInB.Should().BeFalse("cache key must NOT exist in container B (the lock Redis instance)");

        // --- assert: lock key is in container B, NOT in container A ---
        // Lock physical keys follow the pattern "{hflock:<hex>}:value"; scan for any such key.
        var lockKeyInA = await _KeyExistsByPatternAsync(_cacheMultiplexer, "{hflock:*}:value", ct);
        var lockKeyInB = await _KeyExistsByPatternAsync(_lockMultiplexer, "{hflock:*}:value", ct);
        lockKeyInA.Should().BeFalse("lock key must NOT exist in container A (the cache Redis instance)");
        lockKeyInB.Should().BeTrue("lock key must exist in container B (the lock Redis instance)");

        await lockHandle!.DisposeAsync();
    }

    private static async Task<bool> _KeyExistsByPatternAsync(
        ConnectionMultiplexer multiplexer,
        string pattern,
        CancellationToken ct
    )
    {
        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            var server = multiplexer.GetServer(endpoint);
            if (server.IsReplica || !server.IsConnected)
            {
                continue;
            }

            await foreach (var _ in server.KeysAsync(pattern: pattern).WithCancellation(ct))
            {
                return true;
            }
        }

        return false;
    }
}
