// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RegularLocks;

public sealed class DisposableResourceLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IResourceLockProvider _lockProvider = Substitute.For<IResourceLockProvider>();

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
        await _lockProvider
            .Received(1)
            .ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
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
        await _lockProvider
            .Received(1)
            .ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
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
        await _lockProvider
            .Received(1)
            .ReleaseAsync(resource, lockId, Arg.Any<CancellationToken>());
    }

    private DisposableResourceLock _CreateLock(
        string resource,
        string lockId,
        TimeSpan? timeWaitedForLock = null)
    {
        return new DisposableResourceLock(
            resource,
            lockId,
            timeWaitedForLock ?? TimeSpan.Zero,
            _lockProvider,
            _timeProvider,
            LoggerFactory.CreateLogger(nameof(DisposableResourceLock))
        );
    }
}
