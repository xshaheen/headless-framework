// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for <see cref="ICacheProvider"/> keyed-service resolution.</summary>
public sealed class CacheProviderTests : TestBase
{
    [Fact]
    public async Task should_resolve_cache_registered_under_known_name()
    {
        // given
        var named = Substitute.For<ICache>();
        var services = new ServiceCollection();
        services.AddKeyedSingleton("orders", named);
        services.AddCacheProvider();
        await using var provider = services.BuildServiceProvider();
        var cacheProvider = provider.GetRequiredService<ICacheProvider>();

        // when & then
        cacheProvider.GetCache("orders").Should().BeSameAs(named);
        cacheProvider.GetCacheOrNull("orders").Should().BeSameAs(named);
    }

    [Fact]
    public async Task should_resolve_role_keys()
    {
        // given
        var memory = Substitute.For<ICache>();
        var remote = Substitute.For<ICache>();
        var services = new ServiceCollection();
        services.AddKeyedSingleton(CacheConstants.MemoryCacheProvider, memory);
        services.AddKeyedSingleton(CacheConstants.RemoteCacheProvider, remote);
        services.AddCacheProvider();
        await using var provider = services.BuildServiceProvider();
        var cacheProvider = provider.GetRequiredService<ICacheProvider>();

        // when & then
        cacheProvider.GetCache(CacheConstants.MemoryCacheProvider).Should().BeSameAs(memory);
        cacheProvider.GetCache(CacheConstants.RemoteCacheProvider).Should().BeSameAs(remote);
    }

    [Fact]
    public async Task should_return_null_for_unknown_name_from_get_cache_or_null()
    {
        // given
        var services = new ServiceCollection();
        services.AddCacheProvider();
        await using var provider = services.BuildServiceProvider();
        var cacheProvider = provider.GetRequiredService<ICacheProvider>();

        // when & then
        cacheProvider.GetCacheOrNull("unknown").Should().BeNull();
    }

    [Fact]
    public async Task should_throw_for_unknown_name_from_get_cache()
    {
        // given
        var services = new ServiceCollection();
        services.AddCacheProvider();
        await using var provider = services.BuildServiceProvider();
        var cacheProvider = provider.GetRequiredService<ICacheProvider>();

        // when
        var act = () => cacheProvider.GetCache("unknown");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*'unknown'*Register*");
    }

    [Fact]
    public void add_cache_provider_should_be_idempotent()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddCacheProvider();
        services.AddCacheProvider();

        // then
        services.Count(d => d.ServiceType == typeof(ICacheProvider)).Should().Be(1);
    }
}
