// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A hybrid cache needs a backplane before the host starts: without one, writes on this host leave the other hosts
/// serving stale L1 entries, so the host refuses to start and names every choice.
/// </summary>
public sealed class HybridCacheBackplaneRegistrationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_fail_startup_naming_every_backplane_choice_when_none_is_chosen()
    {
        // given
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var services = _CreateServices(new InMemoryRemoteCacheAdapter(l2), static _ => { });

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<MissingRequiredServiceException>()).Which;
        exception.MissingServices.Should().ContainSingle().Which.ServiceType.Should().Be<IHybridCacheBackplane>();
        exception
            .Message.Should()
            .Contain("setup.UseRedisBackplane() (package Headless.Caching.Redis)")
            .And.Contain("setup.UseMessagingBackplane() (package Headless.Caching.Hybrid.Messaging)")
            .And.Contain("setup.UseSingleInstanceBackplane()")
            .And.Contain(nameof(IHybridCacheBackplane));
    }

    [Fact]
    public async Task should_fail_resolving_the_hybrid_naming_every_choice_when_the_startup_check_never_ran()
    {
        // given - a container built without running the host's startup checks
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        await using var provider = _CreateServices(new InMemoryRemoteCacheAdapter(l2), static _ => { })
            .BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<ICache>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*setup.UseRedisBackplane()*setup.UseMessagingBackplane()*setup.UseSingleInstanceBackplane()*"
            );
    }

    [Fact]
    public async Task should_start_and_send_nothing_when_the_host_declares_a_single_instance()
    {
        // given
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var services = _CreateServices(
            new InMemoryRemoteCacheAdapter(l2),
            static setup => setup.UseSingleInstanceBackplane()
        );

        // when
        await _RunStartingAsync(services);
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICache>();
        await cache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // then
        provider.GetRequiredService<IHybridCacheBackplane>().Should().BeOfType<SingleInstanceHybridCacheBackplane>();
        (await cache.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
    }

    [Fact]
    public async Task should_send_through_a_custom_backplane_registered_after_the_caching_setup()
    {
        // given - the backplane is checked when the host starts, so registration order does not matter
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var services = _CreateServices(new InMemoryRemoteCacheAdapter(l2), static _ => { });
        var backplane = Substitute.For<IHybridCacheBackplane>();
        services.AddSingleton(backplane);

        // when
        await _RunStartingAsync(services);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ICache>().UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // then
        await backplane
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(message => message.Key == "key"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_register_one_listener_for_the_default_and_a_named_hybrid()
    {
        // given
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var services = _CreateServices(
            new InMemoryRemoteCacheAdapter(l2),
            setup =>
            {
                setup.AddNamed("tenant-l1", instance => instance.UseInMemory(static _ => { }));
                setup.AddNamed(
                    "tenant",
                    instance => instance.UseHybrid(options => options.LocalCacheName = "tenant-l1")
                );
                setup.UseSingleInstanceBackplane();
            }
        );

        // when
        await using var provider = services.BuildServiceProvider();

        // then - one router serves every hybrid by routing on the message's cache name
        provider
            .GetServices<IHybridCacheInvalidationListener>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<HybridCacheInvalidationRouter>();
    }

    private ServiceCollection _CreateServices(IRemoteCache remote, Action<HeadlessCachingSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddLogging();
        services.AddHeadlessCaching(setup =>
        {
            setup.AddMemoryTier();
            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                svc =>
                {
                    svc.AddSingleton(remote);
                    svc.AddKeyedSingleton<ICache>(CacheConstants.RemoteCacheProvider, remote);
                }
            );
            setup.UseHybrid();
            configure(setup);
        });

        return services;
    }

    // The checks ship as an internal IHostedLifecycleService, so drive them the way the host does.
    private static async Task _RunStartingAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();

        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(AbortToken);
        }
    }
}
