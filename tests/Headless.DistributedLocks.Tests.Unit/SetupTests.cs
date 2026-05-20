// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fakes;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_register_lock_provider_without_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedLockProvider>().Should().NotBeNull();
        provider.GetService<IOutboxPublisher>().Should().BeNull();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ConsumerMetadata));
    }

    [Fact]
    public void should_register_lock_released_consumer_when_messaging_is_present()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IOutboxPublisher>());

        // when
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedLockProvider>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxPublisher>().Should().NotBeNull();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ConsumerMetadata));
    }
}
