// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class NamedInMemoryCacheTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);

        services.AddHeadlessCaching(setup =>
        {
            setup.UseInMemory();

            setup.AddNamed(
                "orders",
                instance =>
                    instance.UseInMemory(options =>
                    {
                        options.MaxItems = 100;
                        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
                    })
            );

            setup.AddNamed(
                "catalog",
                instance =>
                    instance.UseInMemory(options =>
                    {
                        options.MaxItems = 50;
                        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(30) };
                    })
            );
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task named_instances_should_be_isolated_from_each_other_and_from_default()
    {
        // given
        await using var provider = _BuildProvider();
        var orders = provider.GetRequiredKeyedService<ICache>("orders");
        var catalog = provider.GetRequiredKeyedService<ICache>("catalog");
        var defaultCache = provider.GetRequiredService<ICache>();

        // when
        await orders.UpsertAsync("key", "orders-value", TimeSpan.FromMinutes(5), AbortToken);

        // then
        (await orders.GetAsync<string>("key", AbortToken))
            .Value.Should()
            .Be("orders-value");
        (await catalog.GetAsync<string>("key", AbortToken)).HasValue.Should().BeFalse();
        (await defaultCache.GetAsync<string>("key", AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task named_instances_should_honor_their_own_default_entry_options()
    {
        // given
        await using var provider = _BuildProvider();
        var orders = provider.GetRequiredKeyedService<ICache>("orders");
        var catalog = provider.GetRequiredKeyedService<ICache>("catalog");

        orders.DefaultEntryOptions.Should().NotBeNull();
        orders.DefaultEntryOptions!.Value.Duration.Should().Be(TimeSpan.FromMinutes(5));
        catalog.DefaultEntryOptions!.Value.Duration.Should().Be(TimeSpan.FromMinutes(30));

        // when - the no-options GetOrAddAsync extension uses each instance's defaults
        await orders.GetOrAddAsync<string>("key", _ => ValueTask.FromResult<string?>("v1"), AbortToken);
        await catalog.GetOrAddAsync<string>("key", _ => ValueTask.FromResult<string?>("v2"), AbortToken);

        // then
        var ordersExpiration = await orders.GetExpirationAsync("key", AbortToken);
        var catalogExpiration = await catalog.GetExpirationAsync("key", AbortToken);
        ordersExpiration.Should().NotBeNull();
        ordersExpiration!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
        catalogExpiration.Should().NotBeNull();
        catalogExpiration!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task named_registration_should_not_disturb_default_cache()
    {
        // given
        await using var provider = _BuildProvider();

        // when
        var defaultCache = provider.GetRequiredService<ICache>();
        var orders = provider.GetRequiredKeyedService<ICache>("orders");
        var roleKeyed = provider.GetRequiredKeyedService<ICache>(CacheConstants.MemoryCacheProvider);

        // then - the unkeyed default and role key still resolve, and are not the named instance
        defaultCache.Should().NotBeSameAs(orders);
        roleKeyed.Should().BeSameAs(defaultCache);
        defaultCache.DefaultEntryOptions.Should().BeNull();
    }

    [Fact]
    public async Task cache_provider_should_resolve_named_instances_and_role_keys()
    {
        // given
        await using var provider = _BuildProvider();
        var cacheProvider = provider.GetRequiredService<ICacheProvider>();

        // when & then
        cacheProvider.GetCache("orders").Should().BeSameAs(provider.GetRequiredKeyedService<ICache>("orders"));
        cacheProvider
            .GetCache(CacheConstants.MemoryCacheProvider)
            .Should()
            .BeSameAs(provider.GetRequiredService<ICache>());
        cacheProvider.GetCacheOrNull("unknown").Should().BeNull();

        var act = () => cacheProvider.GetCache("unknown");
        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown*");
    }

    // Reserved-name and whitespace-name rejection is owned by AddHeadlessCaching's AddNamed gate and is
    // covered by Headless.Caching.Core.Tests.Unit/CachingSetupBuilderTests.
}
