// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Hosts that share one Redis with different <c>Headless:Prefix</c> values must never see, overwrite, or delete each
/// other's keys, and must not hear each other's invalidations.
/// </summary>
[Collection(nameof(RedisCacheFixture))]
public sealed class HeadlessPrefixCacheTests(RedisCacheFixture fixture) : TestBase
{
    [Fact]
    public async Task should_put_the_host_prefix_once_in_front_of_the_default_and_every_named_cache()
    {
        // given
        var prefix = _Prefix();
        using var host = _BuildHost(prefix);
        var key = Faker.Random.AlphaNumeric(12);

        // when
        await host
            .Services.GetRequiredService<ICache>()
            .UpsertAsync(key, "default", TimeSpan.FromMinutes(5), AbortToken);
        var cacheProvider = host.Services.GetRequiredService<ICacheProvider>();
        await cacheProvider.GetCache("orders").UpsertAsync(key, "orders", TimeSpan.FromMinutes(5), AbortToken);
        await cacheProvider.GetCache("catalog").UpsertAsync(key, "catalog", TimeSpan.FromMinutes(5), AbortToken);

        // then - each instance's own prefix sits after exactly one host prefix
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        (await db.KeyExistsAsync($"{prefix}:app:{key}")).Should().BeTrue();
        (await db.KeyExistsAsync($"{prefix}:orders:{key}")).Should().BeTrue();
        (await db.KeyExistsAsync($"{prefix}:catalog:{key}")).Should().BeTrue();
        (await db.KeyExistsAsync($"{prefix}:{prefix}:app:{key}")).Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_two_prefixed_hosts_on_one_redis_apart()
    {
        // given - two environments with the same cache configuration and the same key
        using var first = _BuildHost(_Prefix());
        using var second = _BuildHost(_Prefix());
        var firstCache = first.Services.GetRequiredService<ICache>();
        var secondCache = second.Services.GetRequiredService<ICache>();
        const string key = "shared-key";

        // when
        await firstCache.UpsertAsync(key, "first", TimeSpan.FromMinutes(5), AbortToken);
        await secondCache.UpsertAsync(key, "second", TimeSpan.FromMinutes(5), AbortToken);
        await firstCache.ClearAsync(AbortToken);

        // then - the first environment's clear reached none of the second's keys
        (await firstCache.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await secondCache.GetAsync<string>(key, AbortToken)).Value.Should().Be("second");
    }

    [Fact]
    public void should_put_the_host_prefix_in_front_of_the_invalidation_channel()
    {
        // given
        var prefix = _Prefix();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.ConfigureHeadlessPrefix(prefix);
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.AddMemoryTier();
            setup.AddRedisTier(options =>
            {
                options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                options.KeyPrefix = "app:";
            });
            setup.UseHybrid();
            setup.UseRedisBackplane();
        });
        using var host = builder.Build();

        // when
        var backplane = (RedisHybridCacheBackplane)host.Services.GetRequiredService<IHybridCacheBackplane>();

        // then
        backplane.Channel.ToString().Should().Be($"{prefix}:app:{CacheInvalidationMessage.MessageName}");
    }

    private IHost _BuildHost(string prefix)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.ConfigureHeadlessPrefix(prefix);
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options =>
            {
                options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                options.KeyPrefix = "app:";
            });

            setup.AddNamed(
                "orders",
                instance =>
                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                        options.KeyPrefix = "orders:";
                    })
            );

            setup.AddNamed(
                "catalog",
                instance =>
                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                        options.KeyPrefix = "catalog:";
                    })
            );
        });

        return builder.Build();
    }

    private string _Prefix() => "env" + Faker.Random.AlphaNumeric(8).ToLowerInvariant();
}
