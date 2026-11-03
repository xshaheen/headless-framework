// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Covers <c>UseMessagingBackplane()</c>: it contributes the package's generated messaging module, the registration order
/// of caching and messaging does not matter, the contribution stays inert without messaging, the consumer registers once
/// as an every-instance Bus consumer, and the last backplane choice wins.
/// </summary>
public sealed class SetupTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public void should_contribute_only_an_inert_module_when_messaging_is_absent()
    {
        // given
        var services = _CreateServices();
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());

        // when
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2), static setup => setup.UseMessagingBackplane());

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
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2), static setup => setup.UseMessagingBackplane());
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
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2), static setup => setup.UseMessagingBackplane());
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
            setup.UseMessagingBackplane();
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
        _AddDefaultHybrid(services, new InMemoryRemoteCacheAdapter(l2), static setup => setup.UseMessagingBackplane());
        services.AddHeadlessMessaging(_ => { });

        // when
        services.ConfigureMessaging(messaging =>
            messaging.Tune(HybridCacheInvalidationConsumer.Identity, consumer => consumer.Concurrency(4))
        );
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        _AssertSingleInvalidationConsumer(provider).Concurrency.Should().Be(4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_keep_the_last_backplane_choice(bool messagingLast)
    {
        // given
        var services = _CreateServices();
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());

        // when
        _AddDefaultHybrid(
            services,
            new InMemoryRemoteCacheAdapter(l2),
            setup =>
            {
                if (messagingLast)
                {
                    setup.UseSingleInstanceBackplane();
                    setup.UseMessagingBackplane();
                }
                else
                {
                    setup.UseMessagingBackplane();
                    setup.UseSingleInstanceBackplane();
                }
            }
        );
        using var provider = services.BuildServiceProvider();

        // then
        var backplane = provider.GetRequiredService<IHybridCacheBackplane>();

        if (messagingLast)
        {
            backplane.Should().BeOfType<MessagingHybridCacheBackplane>();
        }
        else
        {
            backplane.GetType().Name.Should().Be("SingleInstanceHybridCacheBackplane");
        }
    }

    [Fact]
    public void should_not_register_the_consumer_when_the_host_chose_another_backplane()
    {
        // given - messaging in the host, but the hybrid's invalidations travel elsewhere
        var services = _CreateServices();
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        _AddDefaultHybrid(
            services,
            new InMemoryRemoteCacheAdapter(l2),
            static setup => setup.UseSingleInstanceBackplane()
        );
        services.AddHeadlessMessaging(_ => { });

        // when
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();

        // then
        provider
            .GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .NotContain(metadata => metadata.ConsumerType == typeof(HybridCacheInvalidationConsumer));
    }

    private ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton(Substitute.For<IBus>());

        return services;
    }

    private static void _AddDefaultHybrid(
        ServiceCollection services,
        InMemoryRemoteCacheAdapter remote,
        Action<HeadlessCachingSetupBuilder>? configure = null
    )
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
            configure?.Invoke(setup);
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
