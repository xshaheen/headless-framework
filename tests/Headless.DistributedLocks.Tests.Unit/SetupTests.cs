// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
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
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
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

        // when — register the lock provider BEFORE AddHeadlessMessaging.
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
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

        // when — the provider registers both lock and semaphore; they share one consumer via the
        // ICanReceiveLockReleased fan-out, so only a single registry entry must exist.
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
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

        // when — registering AFTER AddHeadlessMessaging still works; the captured registration is
        // drained at messaging bootstrap, so registration order does not matter.
        var act = () => services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        act.Should().NotThrow();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();

        // then
        provider
            .GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(metadata => metadata.ConsumerType == typeof(DistributedLock.LockReleasedConsumer));
    }

    [Fact]
    public void should_throw_when_no_provider_is_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        var act = () => services.AddHeadlessDistributedLocks(_ => { });

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseInMemory*UseRedis*UsePostgreSql*UseSqlServer*");
    }

    [Fact]
    public void should_throw_when_multiple_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        var act = () =>
            services.AddHeadlessDistributedLocks(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemory();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_throw_when_add_headless_distributed_locks_is_called_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

        // when
        var act = () => services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }
}
