// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// A multiplexed connection whose unlock (or acquire cleanup) failed may still hold that lock server-side. These tests
/// pin that such a connection is never handed back to the driver's pool: it takes no further acquires, and once its
/// last live lock is released it is destroyed rather than closed.
/// </summary>
public sealed class MultiplexedConnectionLockPoisonTests : TestBase
{
    private const string _ConnectionString = "fake-connection-string";
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<RecordingDatabaseConnection> _connections = [];

    [Fact]
    public async Task should_destroy_the_connection_instead_of_pooling_it_when_the_only_unlock_throws()
    {
        // given
        var strategy = new FakeSynchronizationStrategy { ThrowOnReleaseIdentity = "resource-a" };
        var pool = _CreatePool();
        var handle = await _AcquireAsync(pool, "resource-a", strategy);
        var connection = _connections.Should().ContainSingle().Subject;

        // when
        var release = async () => await handle!.DisposeAsync();

        // then
        await release.Should().ThrowAsync<InvalidOperationException>();
        connection.CloseCount.Should().Be(0, "closing would return a session that may still hold the lock to the pool");
        connection.PoolClearCount.Should().Be(1);
        connection.DisposeCount.Should().BePositive();
    }

    [Fact]
    public async Task should_not_reenter_the_failed_lock_on_the_same_connection_when_its_unlock_threw()
    {
        // given (two locks share one connection; the first one's unlock fails)
        var strategy = new FakeSynchronizationStrategy { ThrowOnReleaseIdentity = "resource-a" };
        var pool = _CreatePool();
        var handleA = await _AcquireAsync(pool, "resource-a", strategy);
        var handleB = await _AcquireAsync(pool, "resource-b", strategy);
        var poisoned = _connections.Should().ContainSingle().Subject;

        var release = async () => await handleA!.DisposeAsync();
        await release.Should().ThrowAsync<InvalidOperationException>();

        // when (acquire the failed resource again, and an unrelated one)
        var again = await _AcquireAsync(pool, "resource-a", strategy);
        var other = await _AcquireAsync(pool, "resource-c", strategy);

        // then (neither lands on the poisoned connection: advisory locks are re-entrant per session, so a re-acquire
        // there would "succeed" against the lock the failed unlock left held)
        again.Should().NotBeNull();
        other.Should().NotBeNull();
        strategy.IsHeld(poisoned.Id, "resource-c").Should().BeFalse();
        _connections.Skip(1).Should().Contain(c => strategy.IsHeld(c.Id, "resource-a"));
        _connections.Skip(1).Should().Contain(c => strategy.IsHeld(c.Id, "resource-c"));

        strategy.ThrowOnReleaseIdentity = null;
        await again!.DisposeAsync();
        await other!.DisposeAsync();
        await handleB!.DisposeAsync();
    }

    [Fact]
    public async Task should_destroy_the_connection_when_acquire_cleanup_fails()
    {
        // given
        var strategy = new FakeSynchronizationStrategy
        {
            ThrowOnAcquire = new LockCleanupFailedException(
                [new TimeoutException("acquire"), new TimeoutException("cleanup")],
                "cleanup failed"
            ),
        };
        var pool = _CreatePool();

        // when
        var acquire = async () => await _AcquireAsync(pool, "resource-a", strategy);

        // then
        await acquire.Should().ThrowAsync<LockCleanupFailedException>();
        var connection = _connections.Should().ContainSingle().Subject;
        connection.CloseCount.Should().Be(0);
        connection.PoolClearCount.Should().Be(1);
        connection.DisposeCount.Should().BePositive();
    }

    [Fact]
    public async Task should_dispose_without_clearing_the_pool_when_the_connection_broke_during_the_failed_unlock()
    {
        // given (the session died mid-unlock, so the server already dropped its locks and the driver will not pool it)
        var strategy = new FakeSynchronizationStrategy
        {
            ThrowOnReleaseIdentity = "resource-a",
            BreakConnectionOnFailedRelease = true,
        };
        var pool = _CreatePool();
        var handle = await _AcquireAsync(pool, "resource-a", strategy);
        var connection = _connections.Should().ContainSingle().Subject;

        // when
        var release = async () => await handle!.DisposeAsync();

        // then (clearing would drop every idle connection of a possibly shared pool for no gain)
        await release.Should().ThrowAsync<InvalidOperationException>();
        connection.PoolClearCount.Should().Be(0);
        connection.DisposeCount.Should().BePositive();
    }

    private MultiplexedConnectionLockPool _CreatePool()
    {
        return new MultiplexedConnectionLockPool(_ =>
        {
            var connection = RecordingDatabaseConnection.CreateClosed(_timeProvider);
            _connections.Add(connection);

            return connection;
        });
    }

    private async Task<IDistributedLease?> _AcquireAsync(
        MultiplexedConnectionLockPool pool,
        string resource,
        FakeSynchronizationStrategy strategy
    )
    {
        return await pool.TryAcquireAsync(
            _ConnectionString,
            resource,
            _Timeout,
            strategy,
            keepaliveCadence: Timeout.InfiniteTimeSpan,
            AbortToken
        );
    }
}
