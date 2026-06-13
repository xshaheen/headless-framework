// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupCachingDistributedLocksTests : TestBase
{
    [Fact]
    public void should_register_singleton_factory_lock_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLock>());

        // when
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.UseDistributedFactoryLock();
        });
        using var serviceProvider = services.BuildServiceProvider();

        // then
        var provider = serviceProvider.GetService<ICacheFactoryLockProvider>();
        provider.Should().NotBeNull();
        provider.Should().BeOfType<DistributedLockCacheFactoryLockProvider>();
        serviceProvider.GetRequiredService<ICacheFactoryLockProvider>().Should().BeSameAs(provider);
    }

    [Fact]
    public void should_apply_setup_action_to_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLock>());

        // when
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.UseDistributedFactoryLock(options =>
            {
                options.ResourcePrefix = "custom:";
                options.TimeUntilExpires = TimeSpan.FromMinutes(2);
            });
        });

        using var serviceProvider = services.BuildServiceProvider();

        // then
        var options = serviceProvider.GetRequiredService<CacheFactoryLockOptions>();
        options.ResourcePrefix.Should().Be("custom:");
        options.TimeUntilExpires.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void should_not_replace_existing_factory_lock_provider_registration()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLock>());
        var existing = Substitute.For<ICacheFactoryLockProvider>();
        services.AddSingleton(existing);

        // when
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.UseDistributedFactoryLock();
        });
        using var serviceProvider = services.BuildServiceProvider();

        // then — TryAdd semantics keep the caller's registration
        serviceProvider.GetRequiredService<ICacheFactoryLockProvider>().Should().BeSameAs(existing);
    }
}
