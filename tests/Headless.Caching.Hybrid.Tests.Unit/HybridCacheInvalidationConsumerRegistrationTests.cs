// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Covers the auto-registration of <see cref="HybridCacheInvalidationConsumer"/> (#511, #936): every hybrid contributes
/// the assembly's generated messaging module, registration order of caching and messaging does not matter, the
/// contribution stays inert without messaging, and the consumer registers once as an every-instance Bus consumer.
/// </summary>
public sealed class HybridCacheInvalidationConsumerRegistrationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public void should_contribute_only_an_inert_module_when_messaging_is_absent()
    {
        // given
        var services = _CreateServices();
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());

        // when
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));

        // then - the consumer is declared by the generated module, not placed in the container, so a bus-less host pays
        // nothing while a bus added later still gets the consumer
        services
            .Should()
            .Contain(static d =>
                string.Equals(d.ServiceType.Name, "MessagingModuleContribution", StringComparison.Ordinal)
            );
        services.Should().NotContain(d => d.ServiceType == typeof(IConsume<CacheInvalidationMessage>));
        services.Should().NotContain(d => d.ServiceType == typeof(HybridCacheInvalidationConsumer));
    }

    [Fact]
    public void should_register_an_every_instance_bus_consumer_when_the_hybrid_precedes_messaging()
    {
        // given - caching first, messaging after: the reversed order once left the backplane publish-only
        var services = _CreateServices();
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));
        services.AddHeadlessMessaging(_ => { });

        // when
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        _AssertSingleInvalidationConsumer(provider);
    }

    [Fact]
    public void should_register_an_every_instance_bus_consumer_when_the_hybrid_follows_messaging()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessMessaging(_ => { });
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());

        // when
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        _AssertSingleInvalidationConsumer(provider);
    }

    [Fact]
    public void should_register_one_consumer_for_the_default_and_a_named_hybrid()
    {
        // given - one consumer routes to every hybrid by CacheName, however many hybrids contribute it
        var services = _CreateServices();
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var namedL2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(namedL2));

        // when
        services.AddHeadlessCaching(setup =>
        {
            setup.AddMemoryTier();
            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                svc =>
                {
                    var remote = new InMemoryRemoteCacheAdapter(l2);
                    svc.AddSingleton<IRemoteCache>(remote);
                    svc.AddKeyedSingleton<ICache>(CacheConstants.RemoteCacheProvider, remote);
                }
            );
            setup.UseHybrid();
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
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        _AssertSingleInvalidationConsumer(provider);
    }

    [Fact]
    public void should_let_the_host_tune_the_consumer_by_its_identity()
    {
        // given
        var services = _CreateServices();
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2));
        services.AddHeadlessMessaging(_ => { });

        // when
        services.ConfigureMessaging(messaging =>
            messaging.Tune(HybridCacheInvalidationConsumer.Identity, consumer => consumer.Concurrency(4))
        );
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        _AssertSingleInvalidationConsumer(provider).Concurrency.Should().Be(4);
    }

    private ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton(Substitute.For<IBus>());

        return services;
    }

    private static void _AddDefaultHybrid(ServiceCollection services, InMemoryRemoteCacheAdapter remote)
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

    // Selecting candidates applies the recorded contributions to the registry, as the bootstrapper does at startup.
    private static ConsumerMetadata _AssertSingleInvalidationConsumer(IServiceProvider provider)
    {
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();
        var metadata = provider
            .GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(m => m.ConsumerType == typeof(HybridCacheInvalidationConsumer))
            .Subject;

        metadata.Lane.Should().Be(MessageLane.Bus);
        metadata.ConsumerIdentity.Should().Be("headless.caching.hybrid.invalidation");
        metadata.EveryInstance.Should().BeTrue();
        metadata.MessageType.Should().Be<CacheInvalidationMessage>();
        metadata.MessageContractVersion.Should().Be("1");

        return metadata;
    }
}
