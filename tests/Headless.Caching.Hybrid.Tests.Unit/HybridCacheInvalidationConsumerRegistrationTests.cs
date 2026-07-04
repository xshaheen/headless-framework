// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Covers the auto-registration of <see cref="HybridCacheInvalidationConsumer"/> (#511): a hybrid cache wires the
/// backplane consumer by default when a messaging bus is present, stays inert without one, and never
/// double-registers when the application already wired the consumer itself.
/// </summary>
public sealed class HybridCacheInvalidationConsumerRegistrationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private ServiceCollection _CreateServices(bool withBus)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_timeProvider);

        if (withBus)
        {
            services.AddSingleton(Substitute.For<IBus>());
        }

        return services;
    }

    private void _AddDefaultHybrid(ServiceCollection services, InMemoryRemoteCacheAdapter remote)
    {
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
    }

    [Fact]
    public void default_hybrid_with_bus_present_auto_registers_invalidation_consumer()
    {
        // given - a bus is present in the container before the hybrid is registered
        var services = _CreateServices(withBus: true);
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());

        // when
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));

        // then - UseHybrid auto-wires the backplane consumer, so cross-node L1 invalidation works by default
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IConsume<CacheInvalidationMessage>));
    }

    [Fact]
    public void default_hybrid_without_bus_does_not_register_invalidation_consumer()
    {
        // given - no messaging bus is registered (single-node host with no backplane)
        var services = _CreateServices(withBus: false);
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());

        // when
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));

        // then - the consumer is not wired, so there is no idle subscription to pay for
        services
            .Should()
            .NotContain(descriptor => descriptor.ServiceType == typeof(IConsume<CacheInvalidationMessage>));
    }

    [Fact]
    public void named_hybrid_with_bus_present_auto_registers_invalidation_consumer()
    {
        // given - a hybrid registered only as a named instance (the default provider is a plain memory cache)
        var services = _CreateServices(withBus: true);
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2));

        // when
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseHybrid(options =>
                    {
                        options.LocalCacheName = "tenant-l1";
                        options.RemoteCacheName = "tenant-l2";
                    })
            );
        });

        // then - the named-hybrid path wires the shared consumer too
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IConsume<CacheInvalidationMessage>));
    }

    [Fact]
    public void hybrid_with_bus_drains_a_single_bus_consumer_into_the_registry()
    {
        // given - a hybrid with a bus, then messaging
        var services = _CreateServices(withBus: true);
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));
        services.AddHeadlessMessaging(_ => { });

        // when - the captured registration is drained into the consumer registry at bootstrap
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();

        // then - exactly one hybrid invalidation consumer is wired, as a broadcast (bus) consumer
        services
            .Count(descriptor => descriptor.ServiceType == typeof(IConsume<CacheInvalidationMessage>))
            .Should()
            .Be(1);
        var metadata = provider
            .GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(m => m.ConsumerType == typeof(HybridCacheInvalidationConsumer))
            .Subject;
        metadata.IntentType.Should().Be(IntentType.Bus);
    }

    [Fact]
    public void hybrid_with_bus_and_app_registered_consumer_registers_exactly_one()
    {
        // given - the application wires the consumer itself, following the advisor's recommended snippet
        var services = _CreateServices(withBus: true);
        services.ForMessage<CacheInvalidationMessage>(message => message.OnBus<HybridCacheInvalidationConsumer>());

        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());

        // when - the hybrid setup runs; its idempotency guard sees the existing consumer and does not re-register
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));
        services.AddHeadlessMessaging(_ => { });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();

        // then - still exactly one consumer, no duplicate
        services
            .Count(descriptor => descriptor.ServiceType == typeof(IConsume<CacheInvalidationMessage>))
            .Should()
            .Be(1);
        provider
            .GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(m => m.ConsumerType == typeof(HybridCacheInvalidationConsumer));
    }
}
