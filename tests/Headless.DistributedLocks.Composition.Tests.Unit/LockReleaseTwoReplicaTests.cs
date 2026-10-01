// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Two hosts on one shared in-memory transport and one shared lock store stand in for two replicas of one application.
/// A waiter lives in one process, so the release signal must reach every replica rather than one of them (#936).
/// </summary>
public sealed class LockReleaseTwoReplicaTests : TestBase
{
    [Fact]
    public async Task should_wake_a_waiter_on_the_other_replica_when_a_lock_is_released()
    {
        // given - the first replica holds the lock; the second waits on a clock that never advances, so its backoff
        // poll never fires and only the release signal can wake it
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        var store = new InMemoryDistributedLockStorage(TimeProvider.System);
        await using var holder = await _StartReplicaAsync(transport, store, TimeProvider.System);
        await using var waiter = await _StartReplicaAsync(transport, store, new FakeTimeProvider());
        var resource = Faker.Random.AlphaNumeric(10);
        var lease = await holder
            .GetRequiredService<IDistributedLock>()
            .TryAcquireAsync(resource, cancellationToken: AbortToken);
        lease.Should().NotBeNull();

        using var waitTimeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        waitTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var waiting = waiter
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(5) },
                waitTimeout.Token
            );

        // Let the waiter fail its first attempt and park on the release signal.
        await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
        waiting.IsCompleted.Should().BeFalse();

        // when
        await holder.GetRequiredService<IDistributedLock>().ReleaseAsync(resource, lease!.LeaseId, AbortToken);

        // then
        var acquired = await waiting;
        acquired.Resource.Should().Be(resource);
    }

    private static async Task<ServiceProvider> _StartReplicaAsync(
        MemoryQueue transport,
        InMemoryDistributedLockStorage store,
        TimeProvider timeProvider
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider);

        // Registered before the provider, whose TryAdd keeps this one store shared by both replicas.
        services.AddSingleton(store);
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        });

        // Registered last, so both replicas resolve the one transport instead of their own.
        services.AddSingleton(transport);

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }
}
