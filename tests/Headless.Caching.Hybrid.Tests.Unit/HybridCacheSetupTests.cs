// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Happy-path coverage for the default-hybrid recipe: tiers + <c>UseHybrid</c>.</summary>
public sealed class HybridCacheSetupTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task default_hybrid_should_compose_memory_tier_and_remote_tier()
    {
        // given - a memory tier plus a fake remote tier (the Redis-backed equivalent of AddRedisTier is
        // exercised in the Redis integration suite), composed under a default hybrid
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton(Substitute.For<IBus>());

        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var remote = new InMemoryRemoteCacheAdapter(l2Inner);

        services.AddHeadlessCaching(setup =>
        {
            setup.AddMemoryTier();
            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                svc =>
                {
                    svc.AddSingleton<IRemoteCache>(remote);
                    svc.AddKeyedSingleton<ICache>(CacheConstants.RemoteCacheProvider, remote);
                }
            );
            setup.UseHybrid();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var defaultCache = provider.GetRequiredService<ICache>();

        // then - the unkeyed default is the hybrid, aliased under the hybrid role key
        var hybrid = defaultCache.Should().BeOfType<HybridCache>().Subject;
        provider.GetRequiredKeyedService<ICache>(CacheConstants.HybridCacheProvider).Should().BeSameAs(defaultCache);

        // then - the local tier is the memory tier's IInMemoryCache, also reachable via the memory role key
        var memoryTier = provider.GetRequiredService<IInMemoryCache>();
        hybrid.LocalCache.Should().BeSameAs(memoryTier);
        provider.GetRequiredKeyedService<ICache>(CacheConstants.MemoryCacheProvider).Should().BeSameAs(memoryTier);

        // then - the generic cache adapter resolves over the default hybrid
        provider.GetRequiredService<ICache<HybridCacheSetupTests>>().Should().NotBeNull();

        // then - writes go through both tiers
        await hybrid.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        (await memoryTier.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
        (await l2Inner.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
    }
}
