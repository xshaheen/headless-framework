// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Caching;
using Headless.Serializer;
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
                setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
                setup.RegisterDefaultProvider(CacheConstants.RemoteCacheProvider, static _ => { });
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
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { })
        );

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { })
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
            setup.RegisterCrossCuttingExtension(_ => log.Add("cross-cutting"));
            setup.AddNamed("orders", instance => instance.RegisterProvider(_ => log.Add("named")));
            setup.RegisterDefaultProvider(CacheConstants.HybridCacheProvider, _ => log.Add("default"));
            setup.RegisterTierProvider(CacheConstants.MemoryCacheProvider, _ => log.Add("tier-memory"));
            setup.RegisterTierProvider(CacheConstants.RemoteCacheProvider, _ => log.Add("tier-remote"));
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
                setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
                setup.RegisterTierProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage($"*'{CacheConstants.MemoryCacheProvider}'*");
        services.Should().BeEmpty();
    }

    [Fact]
    public void should_reject_duplicate_names_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
                setup.AddNamed("orders", instance => instance.RegisterProvider(static _ => { }));
                setup.AddNamed("orders", instance => instance.RegisterProvider(static _ => { }));
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'orders'*already configured*");
    }

    [Theory]
    [InlineData(CacheConstants.MemoryCacheProvider)]
    [InlineData(CacheConstants.RemoteCacheProvider)]
    [InlineData(CacheConstants.HybridCacheProvider)]
    [InlineData("Headless.Caching:custom")]
    public void should_reject_reserved_names_when_add_named(string reservedName)
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.AddNamed(reservedName, instance => instance.RegisterProvider(static _ => { }))
            );

        // then
        action.Should().Throw<ArgumentException>().WithMessage($"*'{reservedName}'*reserved*");
    }

    [Fact]
    public void should_reject_whitespace_name_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.AddNamed(" ", instance => instance.RegisterProvider(static _ => { }))
            );

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_zero_providers_when_add_named()
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
    public void should_reject_multiple_providers_when_add_named()
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
                        instance.RegisterProvider(static _ => { });
                        instance.RegisterProvider(static _ => { });
                    }
                )
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*'orders'*");
    }

    [Fact]
    public void should_reject_non_reserved_role_key_when_register_tier()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessCaching(setup => setup.RegisterTierProvider("custom", static _ => { }));

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*'custom'*reserved role keys*");
    }

    [Fact]
    public void should_reject_bare_role_alias_when_register_tier()
    {
        // given - "memory" is a reserved short alias, but the tier role key must be the namespaced constant
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessCaching(setup => setup.RegisterTierProvider("memory", static _ => { }));

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*'memory'*reserved role keys*");
    }

    [Fact]
    public void should_reject_duplicate_role_when_register_tier()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterTierProvider(CacheConstants.MemoryCacheProvider, static _ => { });
                setup.RegisterTierProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*already registered*'{CacheConstants.MemoryCacheProvider}'*");
    }

    [Fact]
    public void should_reject_role_already_claimed_by_default_provider_when_register_tier()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterDefaultProvider(CacheConstants.RemoteCacheProvider, static _ => { });
                setup.RegisterTierProvider(CacheConstants.RemoteCacheProvider, static _ => { });
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{CacheConstants.RemoteCacheProvider}'*default*tier*");
    }

    [Fact]
    public async Task should_be_reachable_via_cache_provider_when_named_extension_keyed_registration()
    {
        // given
        var namedCache = Substitute.For<ICache>();
        var services = new ServiceCollection();
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "orders",
                instance =>
                {
                    var name = instance.Name;
                    instance.RegisterProvider(svc => svc.AddKeyedSingleton(name, namedCache));
                }
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var cacheProvider = provider.GetRequiredService<ICacheProvider>();

        // then
        cacheProvider.GetCache("orders").Should().BeSameAs(namedCache);
    }

    // The public WithSerializer overloads live in the Redis provider package (serialization is a Redis-tier
    // concern); these tests cover the provider-agnostic slot mechanics directly via the internal setter.
    [Fact]
    public void should_capture_serializer_factory_when_named_instance()
    {
        // given
        var serializer = new TestSerializer();
        var instance = new HeadlessCacheInstanceBuilder("orders");

        // when
        instance.SetSerializerFactory(_ => serializer);

        // then
        using var provider = new ServiceCollection().BuildServiceProvider();
        instance.SerializerFactory.Should().NotBeNull();
        instance.SerializerFactory!(provider).Should().BeSameAs(serializer);
    }

    [Fact]
    public void should_reject_multiple_serializer_configurations_when_named_instance()
    {
        // given
        var instance = new HeadlessCacheInstanceBuilder("orders");
        instance.SetSerializerFactory(_ => new TestSerializer());

        // when
        var action = () => instance.SetSerializerFactory(_ => new TestSerializer());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*serializer*already configured*'orders'*");
    }

    private sealed class TestSerializer : ISerializer
    {
        public void Serialize<T>(T value, IBufferWriter<byte> output) => throw new NotSupportedException();

        public void Serialize(object? value, IBufferWriter<byte> output) => throw new NotSupportedException();

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => throw new NotSupportedException();

        public T Deserialize<T>(in ReadOnlySequence<byte> data) => throw new NotSupportedException();

        public object Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();

        public object Deserialize(in ReadOnlySequence<byte> data, Type type) => throw new NotSupportedException();
    }
}
