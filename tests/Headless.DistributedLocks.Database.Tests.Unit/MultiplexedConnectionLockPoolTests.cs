// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// Pure-logic unit tests for the optimistic multiplexing engine: which acquires share an existing connection, which are
/// forced onto a dedicated connection, the cookie-keyed held set (guard #1), and the connection-stays-open-on-release-
/// failure behavior. Real database contention/fallback is covered by the Postgres integration suite.
/// </summary>
public sealed class MultiplexedConnectionLockPoolTests : TestBase
{
    private const string _ConnectionString = "fake-connection-string";
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<RecordingDatabaseConnection> _connections = [];

    [Fact]
    public async Task should_share_one_connection_when_two_distinct_keys_are_acquired_uncontended()
    {
        // given
        var strategy = new FakeSynchronizationStrategy();
        var pool = _CreatePool();

        // when (acquire two distinct resources; the second should reuse the first's connection opportunistically)
        var handleA = await _AcquireAsync(pool, "resource-a", strategy);
        var handleB = await _AcquireAsync(pool, "resource-b", strategy);

        // then
        handleA.Should().NotBeNull();
        handleB.Should().NotBeNull();
        _connections.Should().ContainSingle("both locks multiplex onto one pooled connection");
        _connections[0].OpenCount.Should().Be(1);

        await handleA!.DisposeAsync();
        await handleB!.DisposeAsync();
    }

    [Fact]
    public async Task should_force_a_dedicated_connection_when_the_same_key_is_acquired_twice()
    {
        // given
        var strategy = new FakeSynchronizationStrategy();
        var pool = _CreatePool();

        // when (acquire the same resource twice — the engine refuses to hold one physical lock twice on a connection)
        var first = await _AcquireAsync(pool, "resource-a", strategy);
        var second = await _AcquireAsync(pool, "resource-a", strategy);

        // then (a second connection had to be opened)
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        _connections.Should().HaveCount(2, "the colliding key forces a fresh connection");

        await first!.DisposeAsync();
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task should_force_a_dedicated_connection_when_two_resource_strings_resolve_to_the_same_key()
    {
        // given (guard #1: two distinct strings map to one resolved advisory key)
        var sharedIdentity = new object();
        var strategy = new FakeSynchronizationStrategy();
        strategy.MapIdentity("resource-x", sharedIdentity);
        strategy.MapIdentity("resource-y", sharedIdentity);
        var pool = _CreatePool();

        // when
        var first = await _AcquireAsync(pool, "resource-x", strategy);
        var second = await _AcquireAsync(pool, "resource-y", strategy);

        // then (the second acquirer, despite a different resource string, lands on its own connection because the
        // held-set is keyed by the resolved identity — not the string)
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        _connections.Should().HaveCount(2);
        _connections[0].Id.Should().NotBe(_connections[1].Id);

        await first!.DisposeAsync();
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task should_keep_the_connection_pooled_when_one_of_several_locks_is_released()
    {
        // given
        var strategy = new FakeSynchronizationStrategy();
        var pool = _CreatePool();

        var handleA = await _AcquireAsync(pool, "resource-a", strategy);
        var handleB = await _AcquireAsync(pool, "resource-b", strategy);
        var connection = _connections.Should().ContainSingle().Subject;

        // when (release one of the two)
        await handleA!.DisposeAsync();

        // then (the connection still holds resource-b, so it stays open and is not disposed)
        connection.IsOpen.Should().BeTrue();
        connection.DisposeCount.Should().Be(0);

        // when (release the last)
        await handleB!.DisposeAsync();

        // then (the last release closes the connection so it can be reused/pruned, without disposing it yet)
        connection.CloseCount.Should().BePositive();
        connection.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_close_the_connection_when_a_release_throws_while_other_locks_are_held()
    {
        // given
        var strategy = new FakeSynchronizationStrategy();
        var pool = _CreatePool();

        var handleA = await _AcquireAsync(pool, "resource-a", strategy);
        var handleB = await _AcquireAsync(pool, "resource-b", strategy);
        var connection = _connections.Should().ContainSingle().Subject;

        // resource-a's resolved identity is the resource string itself (no mapping)
        strategy.ThrowOnReleaseIdentity = "resource-a";

        // when (release resource-a; it throws while resource-b is still held)
        var release = async () => await handleA!.DisposeAsync();

        // then (the failure propagates, but the connection is NOT force-closed: closing it would release resource-b's
        // advisory lock server-side without firing resource-b's connection-lost token, leaving its holder believing it
        // still owns a lock another process could take — a mutual-exclusion violation. resource-a lingers held until
        // resource-b releases and the connection closes normally; that bounded latency is the safe trade.)
        await release.Should().ThrowAsync<InvalidOperationException>();
        connection.CloseCount.Should().Be(0, "the connection must stay open while resource-b is still held");
        connection.IsOpen.Should().BeTrue();

        // and (releasing the last held lock closes the connection normally, which releases resource-a server-side too)
        strategy.ThrowOnReleaseIdentity = null;
        await handleB!.DisposeAsync();
        connection.CloseCount.Should().BePositive("the last release closes the connection once nothing is held");
        connection.IsOpen.Should().BeFalse();
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
