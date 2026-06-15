// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection(nameof(BclRedisFixture))]
public sealed class HeadlessDistributedCacheAdapterTests(BclRedisFixture fixture) : TestBase
{
    private const int _RedisFrameHeaderLength = 27;

    [Fact]
    public async Task should_register_bcl_distributed_cache_over_named_redis_cache()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();
        var cacheProvider = host.Services.GetRequiredService<ICacheProvider>();

        distributedCache.Should().BeOfType<HeadlessDistributedCacheAdapter>();
        cacheProvider.GetCache(cacheName).Should().NotBeNull();
    }

    [Fact]
    public async Task should_round_trip_bytes_without_json_or_base64_encoding()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var key = Faker.Random.AlphaNumeric(12);
        byte[] payload = [0, 1, 2, 3, 250, 251, 252, 253, 254, 255];

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();

        await distributedCache.SetAsync(
            key,
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            AbortToken
        );

        (await distributedCache.GetAsync(key, AbortToken)).Should().Equal(payload);

        var stored = await fixture.ConnectionMultiplexer.GetDatabase().StringGetAsync(keyPrefix + key);
        stored.HasValue.Should().BeTrue();
        var storedBytes = (byte[])stored!;
        storedBytes[0].Should().Be(0xFF);
        storedBytes.AsSpan(_RedisFrameHeaderLength).ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task should_support_sync_get_set_refresh_and_remove()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var key = Faker.Random.AlphaNumeric(12);
        byte[] payload = [1, 3, 5, 7];

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();

        distributedCache.Set(
            key,
            payload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                SlidingExpiration = TimeSpan.FromMinutes(1),
            }
        );
        distributedCache.Refresh(key);

        distributedCache.Get(key).Should().Equal(payload);

        distributedCache.Remove(key);

        distributedCache.Get(key).Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_setting_null_bytes()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();

        Func<Task> act = () =>
            distributedCache.SetAsync(
                "null-value",
                null!,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) },
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<IHost> _StartHostAsync(string cacheName, string keyPrefix, TimeSpan defaultAbsoluteExpiration)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
            setup.AddHeadlessDistributedCache(
                options =>
                {
                    options.CacheName = cacheName;
                    options.DefaultAbsoluteExpiration = defaultAbsoluteExpiration;
                },
                instance =>
                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                        options.KeyPrefix = keyPrefix;
                    })
            );
        });

        var host = builder.Build();
        await host.StartAsync(AbortToken);

        return host;
    }
}
