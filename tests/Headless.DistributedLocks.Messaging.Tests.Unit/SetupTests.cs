// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_replace_the_silent_release_signal_with_the_messaging_one()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBus>());

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory().UseMessagingReleaseSignal());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        provider
            .GetRequiredService<IDistributedLockReleaseSignal>()
            .Should()
            .BeOfType<MessagingDistributedLockReleaseSignal>();
    }

    [Fact]
    public void should_register_an_every_instance_lock_released_consumer_when_added_before_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBus>());

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory().UseMessagingReleaseSignal());
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then - the release signal keeps its contract, and every process receives it because the waiters it wakes
        // live in each process
        provider.GetRequiredService<IDistributedLock>().Should().NotBeNull();
        var metadata = _SingleLockReleasedConsumer(provider);
        metadata.MessageName.Should().Be("headless.locks.released");
        metadata.MessageContractVersion.Should().Be("1");
        metadata.ConsumerIdentity.Should().Be("headless.distributed-locks.release");
        metadata.Lane.Should().Be(MessageLane.Bus);
        metadata.EveryInstance.Should().BeTrue();
        metadata.Concurrency.Should().Be(1);
    }

    [Fact]
    public void should_share_one_lock_released_consumer_across_lock_and_semaphore()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBus>());

        // when - the provider registers the lock, semaphore, and reader-writer primitives; they share one consumer
        // through the release-listener fan-out, so only a single registry entry must exist.
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory().UseMessagingReleaseSignal());
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        _SingleLockReleasedConsumer(provider);
        provider.GetRequiredService<IConsumerRegistry>().GetAll().Should().ContainSingle();
    }

    [Fact]
    public void should_register_lock_released_consumer_when_added_after_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(_ => { });

        // when - the contribution is applied when messaging starts, so registration order does not matter
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory().UseMessagingReleaseSignal());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        _SingleLockReleasedConsumer(provider).EveryInstance.Should().BeTrue();
    }

    [Fact]
    public async Task should_publish_the_release_signal_through_the_registered_bus()
    {
        // given
        var bus = Substitute.For<IBus>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory().UseMessagingReleaseSignal());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await locks.TryAcquireAsync(resource, cancellationToken: AbortToken);
        handle.Should().NotBeNull();

        // when
        await locks.ReleaseAsync(resource, handle!.LeaseId, AbortToken);

        // then - the container's bus carries the signal, still autonomous and Direct.
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<DistributedLockReleased>(message =>
                    message.Resource == resource && message.LeaseId == handle.LeaseId
                ),
                Arg.Is<PublishOptions?>(options => options!.DeliveryMode == DeliveryMode.Direct),
                Arg.Any<CancellationToken>()
            );
    }

    // Selecting candidates applies the recorded contributions to the registry, as the bootstrapper does at startup.
    private static ConsumerMetadata _SingleLockReleasedConsumer(IServiceProvider provider)
    {
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();
        return provider
            .GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(m => m.ConsumerType == typeof(DistributedLockReleasedConsumer))
            .Subject;
    }
}
