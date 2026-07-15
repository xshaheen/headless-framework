// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Happy-path coverage for the full default-hybrid recipe over real tiers:
/// <c>AddMemoryTier</c> + <c>AddRedisTier</c> + <c>UseHybrid</c>.
/// </summary>
[Collection(nameof(RedisCacheFixture))]
public sealed class DefaultHybridCacheTiersTests(RedisCacheFixture fixture) : TestBase
{
    [Fact]
    public async Task should_compose_memory_tier_and_redis_tier_when_default_hybrid()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Substitute.For<IBus>());

        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.AddMemoryTier();
            setup.AddRedisTier(options =>
            {
                options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                options.KeyPrefix = "hybrid-tiers:";
            });
            setup.UseHybrid(options => options.DefaultLocalExpiration = TimeSpan.FromMinutes(5));
        });

        using var host = builder.Build();
        await host.StartAsync(AbortToken);

        // when
        var defaultCache = host.Services.GetRequiredService<ICache>();

        // then - the unkeyed default is the hybrid composed from the role-keyed tiers
        var hybrid = defaultCache.Should().BeOfType<HybridCache>().Subject;
        host.Services.GetRequiredKeyedService<ICache>(CacheConstants.HybridCacheProvider).Should().BeSameAs(hybrid);
        hybrid.LocalCache.Should().BeSameAs(host.Services.GetRequiredService<IInMemoryCache>());
        host.Services.GetRequiredService<IRemoteCache>().Should().BeOfType<RedisCache>();

        // then - writes go through both tiers: visible in the memory tier and in Redis under the tier prefix
        var key = Faker.Random.AlphaNumeric(12);
        await hybrid.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        var memoryTier = host.Services.GetRequiredService<IInMemoryCache>();
        (await memoryTier.GetAsync<string>(key, AbortToken)).Value.Should().Be("value");

        var db = fixture.ConnectionMultiplexer.GetDatabase();
        (await db.KeyExistsAsync("hybrid-tiers:" + key)).Should().BeTrue();

        await host.StopAsync(AbortToken);
    }
}
