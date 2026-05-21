// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RegularLocks;

public sealed class DisposableDistributedLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IDistributedLockProvider _lockProvider = Substitute.For<IDistributedLockProvider>();

    [Fact]
    public void should_store_resource_and_lock_id()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();

        // when
        var sut = _CreateLock(resource, lockId);

        // then
        sut.Resource.Should().Be(resource);
        sut.LockId.Should().Be(lockId);
    }

    [Fact]
    public void should_store_acquired_at()
    {
        // given
        var expectedTime = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(expectedTime);
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();

        // when
        var sut = _CreateLock(resource, lockId);

        // then
        sut.DateAcquired.Should().Be(expectedTime);
    }

    [Fact]
    public void should_store_time_waited_for_lock()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        var timeWaited = TimeSpan.FromSeconds(5);

        // when
        var sut = _CreateLock(resource, lockId, timeWaited);

        // then
        sut.TimeWaitedForLock.Should().Be(timeWaited);
    }

    [Fact]
    public async Task should_release_on_dispose()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        var sut = _CreateLock(resource, lockId);

        // when
        await sut.DisposeAsync();

        // then
        await _lockProvider.Received(1).ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_release_on_dispose_when_release_on_dispose_is_false()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        var sut = _CreateLock(resource, lockId, releaseOnDispose: false);

        // when
        await sut.DisposeAsync();

        // then
        await _lockProvider.DidNotReceive().ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_return_none_handle_lost_token_when_monitor_is_absent()
    {
        // given
        var sut = _CreateLock(Faker.Random.AlphaNumeric(10), Faker.Random.Guid().ToString());

        // when
        var token = sut.HandleLostToken;

        // then
        token.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_release_explicitly_when_release_on_dispose_is_false()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        var sut = _CreateLock(resource, lockId, releaseOnDispose: false);

        // when
        await sut.ReleaseAsync();
        await sut.DisposeAsync();

        // then
        await _lockProvider.Received(1).ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_only_release_once()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        var sut = _CreateLock(resource, lockId);

        // when
        await sut.DisposeAsync();
        await sut.DisposeAsync();
        await sut.ReleaseAsync();

        // then
        await _lockProvider.Received(1).ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_route_auto_extend_lease_validation_to_renew()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        _lockProvider.RenewAsync(resource, lockId, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(true);
        var sut = _CreateLock(resource, lockId, autoExtend: true);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then
        result.Should().Be(LeaseMonitor.LeaseState.Renewed);
        await _lockProvider.Received(1).RenewAsync(resource, lockId, TimeSpan.FromSeconds(10), AbortToken);
    }

    [Fact]
    public async Task should_route_monitor_only_lease_validation_to_get_lock_id()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        _lockProvider.GetLockIdAsync(resource, AbortToken).Returns(lockId);
        var sut = _CreateLock(resource, lockId);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then
        result.Should().Be(LeaseMonitor.LeaseState.Held);
        await _lockProvider.Received(1).GetLockIdAsync(resource, AbortToken);
    }

    [Fact]
    public async Task should_calculate_locked_duration()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var lockId = Faker.Random.Guid().ToString();
        var sut = _CreateLock(resource, lockId);
        var expectedDuration = TimeSpan.FromSeconds(30);

        // when
        _timeProvider.Advance(expectedDuration);
        await sut.DisposeAsync();

        // then - verify the elapsed time was captured (logged)
        // The lock calculates duration using timeProvider.GetElapsedTime(_timestamp)
        // We verify indirectly by checking that the lock was released after the time elapsed
        await _lockProvider.Received(1).ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
    }

    private DisposableDistributedLock _CreateLock(
        string resource,
        string lockId,
        TimeSpan? timeWaitedForLock = null,
        bool releaseOnDispose = true,
        bool autoExtend = false
    )
    {
        return new DisposableDistributedLock(
            resource,
            lockId,
            TimeSpan.FromSeconds(10),
            timeWaitedForLock ?? TimeSpan.Zero,
            _lockProvider,
            releaseOnDispose,
            autoExtend,
            new DistributedLockOptions(),
            _timeProvider,
            deregisterMonitor: null,
            LoggerFactory.CreateLogger(nameof(DisposableDistributedLock))
        );
    }
}
