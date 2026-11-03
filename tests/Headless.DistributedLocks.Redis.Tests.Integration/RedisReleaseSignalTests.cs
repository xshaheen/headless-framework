// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Two hosts with their own Redis connections stand in for two replicas of one application. A waiter lives in one
/// process, so the Redis release signal must reach the other replica without any message broker.
/// </summary>
[Collection<RedisTestFixture>]
public sealed class RedisReleaseSignalTests(RedisTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_wake_a_waiter_on_the_other_replica_when_a_lock_is_released()
    {
        // given - the first replica holds the lock; the second waits on a clock that never advances, so its backoff
        // poll never fires and only the release signal can wake it
        var keyPrefix = $"locks-{Faker.Random.AlphaNumeric(8)}:";
        await using var otherConnection = await ConnectionMultiplexer.ConnectAsync(
            fixture.ConnectionMultiplexer.Configuration
        );
        await using var holder = _StartReplica(fixture.ConnectionMultiplexer, keyPrefix, TimeProvider.System);
        await using var waiter = _StartReplica(otherConnection, keyPrefix, new FakeTimeProvider());
        var holderLock = holder.GetRequiredService<IDistributedLock>();
        var waiterLock = waiter.GetRequiredService<IDistributedLock>();
        await _WaitForSubscribersAsync(RedisChannel.Literal(keyPrefix + "released"), expected: 2);

        var resource = Faker.Random.AlphaNumeric(10);
        var lease = await holderLock.TryAcquireAsync(resource, cancellationToken: AbortToken);
        lease.Should().NotBeNull();

        using var waitTimeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        waitTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var waiting = waiterLock.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(5) },
            waitTimeout.Token
        );

        // Let the waiter fail its first attempt and park on the release signal.
        await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
        waiting.IsCompleted.Should().BeFalse();

        // when
        await holderLock.ReleaseAsync(resource, lease!.LeaseId, AbortToken);

        // then
        var acquired = await waiting;
        acquired.Resource.Should().Be(resource);
    }

    [Fact]
    public void should_register_the_redis_signal_in_place_of_the_default()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseRedis().UseRedisReleaseSignal());

        // then
        services
            .Where(x => x.ServiceType == typeof(IDistributedLockReleaseSignal))
            .Should()
            .ContainSingle()
            .Which.ImplementationType.Should()
            .Be<RedisDistributedLockReleaseSignal>();
    }

    [Theory]
    [InlineData("orders:42", "lease-1")]
    [InlineData("a:b:c", "7:x")]
    [InlineData("", "lease")]
    [InlineData("résumé\nline", "")]
    public void should_round_trip_a_release_payload(string resource, string leaseId)
    {
        // when
        var payload = RedisDistributedLockReleaseSignal.EncodePayload(resource, leaseId);
        var decoded = RedisDistributedLockReleaseSignal.TryDecodePayload(
            payload,
            out var decodedResource,
            out var decodedLease
        );

        // then
        decoded.Should().BeTrue();
        decodedResource.Should().Be(resource);
        decodedLease.Should().Be(leaseId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData(":lease")]
    [InlineData("x:lease")]
    [InlineData("-1:lease")]
    [InlineData("99:short")]
    public void should_reject_a_payload_in_another_format(string? payload)
    {
        // when
        var decoded = RedisDistributedLockReleaseSignal.TryDecodePayload(payload, out _, out _);

        // then
        decoded.Should().BeFalse();
    }

    private static ServiceProvider _StartReplica(
        IConnectionMultiplexer connection,
        string keyPrefix,
        TimeProvider timeProvider
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.AddSingleton(connection);
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(options => options.KeyPrefix = keyPrefix);
            setup.UseRedis().UseRedisReleaseSignal();
        });

        return services.BuildServiceProvider();
    }

    // The signal subscribes in the background; a release published before both replicas subscribe would be lost, and
    // the waiter's frozen clock would then never wake it.
    private async Task _WaitForSubscribersAsync(RedisChannel channel, long expected)
    {
        var server = fixture.ConnectionMultiplexer.GetServer(fixture.ConnectionMultiplexer.GetEndPoints()[0]);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (await server.SubscriptionSubscriberCountAsync(channel) < expected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
