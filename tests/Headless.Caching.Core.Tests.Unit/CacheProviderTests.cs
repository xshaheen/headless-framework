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

    [Fact]
    public void registered_names_should_list_named_instances_and_exclude_the_default()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessCaching(static setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed("orders", static instance => instance.RegisterProvider(static _ => { }));
            setup.AddNamed("audit", static instance => instance.RegisterProvider(static _ => { }));
        });
        using var provider = services.BuildServiceProvider();

        // when
        var names = provider.GetRequiredService<ICacheProvider>().RegisteredNames;

        // then - use RegisteredNames to validate externally supplied names before resolving; the default and
        // the role keys are excluded even though GetCache resolves them.
        names.Should().BeEquivalentTo(["orders", "audit"]);
        names.Contains("orders").Should().BeTrue();
        names.Contains(CacheConstants.MemoryCacheProvider).Should().BeFalse();
        names.Contains("nope").Should().BeFalse();
    }
}
