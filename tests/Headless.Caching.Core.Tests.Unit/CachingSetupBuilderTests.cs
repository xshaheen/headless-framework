// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for the <c>AddHeadlessCaching</c> setup builder gates and extension application.</summary>
public sealed class CachingSetupBuilderTests
{
    [Fact]
    public void should_reject_setup_when_default_provider_is_missing()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessCaching(static _ => { });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*exactly one default*UseInMemory*");
    }

    [Fact]
    public void should_reject_setup_when_multiple_default_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterDefaultProvider(
                    CacheConstants.MemoryCacheProvider,
                    new FakeCacheProviderOptionsExtension()
                );
                setup.RegisterDefaultProvider(
                    CacheConstants.RemoteCacheProvider,
                    new FakeCacheProviderOptionsExtension()
                );
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple default providers*");
    }

    [Fact]
    public void should_reject_repeated_provider_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessCaching(setup =>
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, new FakeCacheProviderOptionsExtension())
        );

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.RegisterDefaultProvider(
                    CacheConstants.MemoryCacheProvider,
                    new FakeCacheProviderOptionsExtension()
                )
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_apply_extensions_in_order_tiers_then_default_then_named_then_cross_cutting()
    {
        // given
        var services = new ServiceCollection();
        var log = new List<string>();

        // when
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterCrossCuttingExtension(new RecordingCacheProviderOptionsExtension(log, "cross-cutting"));
            setup.AddNamed(
                "orders",
                instance => instance.RegisterProvider(new RecordingCacheProviderOptionsExtension(log, "named"))
            );
            setup.RegisterDefaultProvider(
                CacheConstants.HybridCacheProvider,
                new RecordingCacheProviderOptionsExtension(log, "default")
            );
            setup.RegisterTierProvider(
                CacheConstants.MemoryCacheProvider,
                new RecordingCacheProviderOptionsExtension(log, "tier-memory")
            );
            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                new RecordingCacheProviderOptionsExtension(log, "tier-remote")
            );
        });

        // then
        log.Should().Equal("tier-memory", "tier-remote", "default", "named", "cross-cutting");
        services.Should().Contain(static descriptor => descriptor.ServiceType == typeof(ICacheProvider));
    }

    [Fact]
    public void should_not_register_services_when_gate_throws()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterDefaultProvider(
                    CacheConstants.MemoryCacheProvider,
                    new FakeCacheProviderOptionsExtension()
                );
                setup.RegisterTierProvider(CacheConstants.MemoryCacheProvider, new FakeCacheProviderOptionsExtension());
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage($"*'{CacheConstants.MemoryCacheProvider}'*");
        services.Should().BeEmpty();
    }

    [Fact]
    public void add_named_should_reject_duplicate_names()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterDefaultProvider(
                    CacheConstants.MemoryCacheProvider,
                    new FakeCacheProviderOptionsExtension()
                );
                setup.AddNamed(
                    "orders",
                    instance => instance.RegisterProvider(new FakeCacheProviderOptionsExtension())
                );
                setup.AddNamed(
                    "orders",
                    instance => instance.RegisterProvider(new FakeCacheProviderOptionsExtension())
                );
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'orders'*already configured*");
    }

    [Theory]
    [InlineData(CacheConstants.MemoryCacheProvider)]
    [InlineData(CacheConstants.RemoteCacheProvider)]
    [InlineData(CacheConstants.HybridCacheProvider)]
    [InlineData("memory")]
    [InlineData("remote")]
    [InlineData("hybrid")]
    [InlineData("Headless.Caching:custom")]
    public void add_named_should_reject_reserved_names(string reservedName)
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.AddNamed(
                    reservedName,
                    instance => instance.RegisterProvider(new FakeCacheProviderOptionsExtension())
                )
            );

        // then
        action.Should().Throw<ArgumentException>().WithMessage($"*'{reservedName}'*reserved*");
    }

    [Fact]
    public void add_named_should_reject_whitespace_name()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.AddNamed(" ", instance => instance.RegisterProvider(new FakeCacheProviderOptionsExtension()))
            );

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void add_named_should_reject_zero_providers()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessCaching(setup => setup.AddNamed("orders", static _ => { }));

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'orders' requires exactly one provider*UseInMemory*");
    }

    [Fact]
    public void add_named_should_reject_multiple_providers()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.AddNamed(
                    "orders",
                    instance =>
                    {
                        instance.RegisterProvider(new FakeCacheProviderOptionsExtension());
                        instance.RegisterProvider(new FakeCacheProviderOptionsExtension());
                    }
                )
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*'orders'*");
    }

    [Fact]
    public void register_tier_should_reject_non_reserved_role_key()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.RegisterTierProvider("custom", new FakeCacheProviderOptionsExtension())
            );

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*'custom'*reserved role keys*");
    }

    [Fact]
    public void register_tier_should_reject_bare_role_alias()
    {
        // given - "memory" is a reserved short alias, but the tier role key must be the namespaced constant
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.RegisterTierProvider("memory", new FakeCacheProviderOptionsExtension())
            );

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*'memory'*reserved role keys*");
    }

    [Fact]
    public void register_tier_should_reject_duplicate_role()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterTierProvider(CacheConstants.MemoryCacheProvider, new FakeCacheProviderOptionsExtension());
                setup.RegisterTierProvider(CacheConstants.MemoryCacheProvider, new FakeCacheProviderOptionsExtension());
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*already registered*'{CacheConstants.MemoryCacheProvider}'*");
    }

    [Fact]
    public void register_tier_should_reject_role_already_claimed_by_default_provider()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterDefaultProvider(
                    CacheConstants.RemoteCacheProvider,
                    new FakeCacheProviderOptionsExtension()
                );
                setup.RegisterTierProvider(CacheConstants.RemoteCacheProvider, new FakeCacheProviderOptionsExtension());
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{CacheConstants.RemoteCacheProvider}'*default*tier*");
    }

    [Fact]
    public async Task named_extension_keyed_registration_should_be_reachable_via_cache_provider()
    {
        // given
        var namedCache = Substitute.For<ICache>();
        var services = new ServiceCollection();
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, new FakeCacheProviderOptionsExtension());
            setup.AddNamed(
                "orders",
                instance => instance.RegisterProvider(new KeyedCacheRegisteringExtension(instance.Name, namedCache))
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var cacheProvider = provider.GetRequiredService<ICacheProvider>();

        // then
        cacheProvider.GetCache("orders").Should().BeSameAs(namedCache);
    }

    private sealed class FakeCacheProviderOptionsExtension : ICacheProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) { }
    }

    private sealed class RecordingCacheProviderOptionsExtension(List<string> log, string id)
        : ICacheProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) => log.Add(id);
    }

    private sealed class KeyedCacheRegisteringExtension(string name, ICache cache) : ICacheProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) => services.AddKeyedSingleton(name, cache);
    }
}
