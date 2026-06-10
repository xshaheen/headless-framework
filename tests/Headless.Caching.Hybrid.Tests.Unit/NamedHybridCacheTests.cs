// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Tests for named hybrid cache registration and named tier binding.</summary>
public sealed class NamedHybridCacheTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private ServiceCollection _CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton(Substitute.For<IBus>());

        return services;
    }

    [Fact]
    public async Task named_hybrid_should_bind_named_tiers()
    {
        // given
        var services = _CreateBaseServices();
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHybridCache(
            "tenant",
            options =>
            {
                options.LocalCacheName = "tenant-l1";
                options.RemoteCacheName = "tenant-l2";
            }
        );

        await using var provider = services.BuildServiceProvider();

        // when
        var cache = provider.GetRequiredKeyedService<ICache>("tenant");

        // then
        var hybrid = cache.Should().BeOfType<HybridCache>().Subject;
        hybrid.LocalCache.Should().BeSameAs(l1);

        await hybrid.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
        (await l2Inner.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
    }

    [Fact]
    public async Task nameless_hybrid_should_bind_named_tiers_when_options_set()
    {
        // given - the options drive tier binding on the default (nameless) path too
        var services = _CreateBaseServices();
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("local-tier", l1);
        services.AddKeyedSingleton<ICache>("remote-tier", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHybridCache(options =>
        {
            options.LocalCacheName = "local-tier";
            options.RemoteCacheName = "remote-tier";
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var hybrid = provider.GetRequiredService<HybridCache>();

        // then
        hybrid.LocalCache.Should().BeSameAs(l1);
    }

    [Fact]
    public async Task named_hybrid_should_fail_clearly_when_named_tier_is_missing()
    {
        // given
        var services = _CreateBaseServices();
        services.AddHybridCache("broken", options => options.LocalCacheName = "missing-tier");
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredKeyedService<ICache>("broken");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*LocalCacheName*'missing-tier'*no cache is registered*");
    }

    [Fact]
    public async Task named_hybrid_should_fail_clearly_when_named_tier_has_wrong_shape()
    {
        // given - "remote-only" is an IRemoteCache, not an IInMemoryCache
        var services = _CreateBaseServices();
        var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("remote-only", new InMemoryRemoteCacheAdapter(l2Inner));
        services.AddHybridCache("bad-shape", options => options.LocalCacheName = "remote-only");
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredKeyedService<ICache>("bad-shape");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*LocalCacheName*'remote-only'*{nameof(IInMemoryCache)}*");
    }

    [Theory]
    [InlineData(CacheConstants.MemoryCacheProvider)]
    [InlineData(CacheConstants.RemoteCacheProvider)]
    [InlineData(CacheConstants.HybridCacheProvider)]
    public void registering_named_hybrid_with_reserved_name_should_throw(string reservedName)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHybridCache(reservedName, _ => { });

        // then
        act.Should().Throw<ArgumentException>().WithMessage($"*{reservedName}*reserved*");
    }
}
