// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_auto_register_lock_released_consumer_even_without_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedLock<InMemoryDistributedLockStorage>(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedLock>().Should().NotBeNull();
        provider.GetService<IOutboxBus>().Should().BeNull();
        // Auto-registration is unconditional. The lock-release consumer descriptor is present even
        // without messaging; without AddHeadlessMessaging it is inert (never drained / dispatched),
        // so waiters fall back to polling.
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>));
    }

    [Fact]
    public void should_register_lock_released_consumer_when_added_before_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IOutboxBus>());

        // when — AddDistributedLock BEFORE AddHeadlessMessaging.
        services.AddDistributedLock<InMemoryDistributedLockStorage>(_ => { });
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();

        // then — the shared lock-release consumer is present in the consumer registry with the
        // expected name, intent, and concurrency, with no explicit opt-in call.
        provider.GetRequiredService<IDistributedLock>().Should().NotBeNull();
        var metadata = provider.GetRequiredService<IConsumerRegistry>().GetAll().Single();
        metadata.ConsumerType.Should().Be<DistributedLock.LockReleasedConsumer>();
        metadata.MessageName.Should().Be("headless.locks.released");
        metadata.IntentType.Should().Be(IntentType.Bus);
        metadata.Concurrency.Should().Be(1);
    }

    [Fact]
    public void should_share_one_lock_released_consumer_across_lock_and_semaphore()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IOutboxBus>());

        // when — both the lock and semaphore providers register; they share one consumer via the
        // ICanReceiveLockReleased fan-out, so only a single registry entry must exist.
        services.AddDistributedLock<InMemoryDistributedLockStorage>(_ => { });
        services.AddDistributedSemaphore<InMemoryDistributedSemaphoreStorage>(_ => { });
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();

        // then
        services
            .Count(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>))
            .Should()
            .Be(1);
        provider.GetRequiredService<IConsumerRegistry>().GetAll().Should().ContainSingle();
    }

    [Fact]
    public void should_register_lock_released_consumer_when_added_after_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(_ => { });

        // when
        var act = () => services.AddDistributedLock<InMemoryDistributedLockStorage>(_ => { });
        act.Should().NotThrow();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();

        // then
        provider.GetRequiredService<IConsumerRegistry>().GetAll().Should().ContainSingle(metadata =>
            metadata.ConsumerType == typeof(DistributedLock.LockReleasedConsumer)
        );
    }

    [Fact]
    public void should_be_idempotent_for_repeated_add_distributed_lock_calls()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — call AddDistributedLock twice.
        services.AddDistributedLock<InMemoryDistributedLockStorage>(_ => { });
        services.AddDistributedLock<InMemoryDistributedLockStorage>(_ => { });

        // then — only one descriptor per service type (TryAdd* semantics) and a single consumer.
        services
            .Count(d => d.ServiceType == typeof(IDistributedLock))
            .Should()
            .Be(1, "TryAddSingleton on IDistributedLock must be idempotent");
        services
            .Count(d => d.ServiceType == typeof(DistributedLock))
            .Should()
            .Be(1, "TryAddSingleton on the concrete DistributedLock must be idempotent");
        services
            .Count(d => d.ServiceType == typeof(IConsume<DistributedLockReleased>))
            .Should()
            .Be(1, "the shared lock-release consumer must be registered exactly once");
    }
}
