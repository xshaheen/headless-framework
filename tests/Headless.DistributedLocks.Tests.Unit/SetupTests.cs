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
    public void should_register_lock_provider_without_messaging_and_skip_lock_released_consumer()
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
        // The LockReleasedConsumer's only job is to wake waiters on DistributedLockReleased outbox
        // messages; without IOutboxPublisher no such messages ever flow, so the consumer is
        // intentionally NOT registered in polling-only mode.
        services
            .Should()
            .NotContain(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>));
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
