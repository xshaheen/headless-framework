// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Happy-path coverage for the default-hybrid recipe: tiers + <c>UseHybrid</c>.</summary>
public sealed class HybridCacheSetupTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_compose_memory_tier_and_remote_tier_when_default_hybrid()
    {
        // given - a memory tier plus a fake remote tier (the Redis-backed equivalent of AddRedisTier is
        // exercised in the Redis integration suite), composed under a default hybrid
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var remote = new InMemoryRemoteCacheAdapter(l2Inner);
        var services = _CreateServices(remote, setup => setup.UseHybrid());

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

    [Fact]
    public async Task should_configure_default_hybrid_with_service_provider_aware_action()
    {
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var remote = new InMemoryRemoteCacheAdapter(l2Inner);
        var services = _CreateServices(
            remote,
            setup =>
                setup.UseHybrid(
                    (options, provider) => options.InstanceId = provider.GetRequiredService<SetupIdentity>().Value
                )
        );
        services.AddSingleton(new SetupIdentity("configured-node"));

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        provider.GetRequiredService<HybridCacheOptions>().InstanceId.Should().Be("configured-node");
        await cache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        (await l2Inner.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
    }

    [Fact]
    public async Task should_bind_default_hybrid_options_from_configuration()
    {
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var remote = new InMemoryRemoteCacheAdapter(l2Inner);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [nameof(HybridCacheOptions.InstanceId)] = "configuration-node",
                    [nameof(HybridCacheOptions.DefaultLocalExpiration)] = "00:03:00",
                }
            )
            .Build();
        var services = _CreateServices(remote, setup => setup.UseHybrid(configuration));

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICache>().Should().BeOfType<HybridCache>();
        var options = provider.GetRequiredService<HybridCacheOptions>();

        options.InstanceId.Should().Be("configuration-node");
        options.DefaultLocalExpiration.Should().Be(TimeSpan.FromMinutes(3));
    }

    private ServiceCollection _CreateServices(IRemoteCache remote, Action<HeadlessCachingSetupBuilder> configureHybrid)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton(Substitute.For<IBus>());
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
            configureHybrid(setup);
        });
        return services;
    }

    private sealed record SetupIdentity(string Value);
}
