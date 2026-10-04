// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// Routing tests for <c>OptimisticConnectionMultiplexingDbDistributedLock</c>: a plain mutex strategy multiplexes onto a
/// shared connection, while an upgradeable strategy or a nested (context-handle) acquire bypasses the pool and uses a
/// dedicated connection.
/// </summary>
public sealed class OptimisticConnectionMultiplexingDbDistributedLockTests : TestBase
{
    private const string _ConnectionString = "fake-connection-string";
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<RecordingDatabaseConnection> _connections = [];

    [Fact]
    public async Task should_multiplex_onto_one_connection_when_the_strategy_is_not_upgradeable()
    {
        // given
        var strategy = new FakeSynchronizationStrategy { IsUpgradeable = false };
        var pool = _CreatePool();

        var lockA = _CreateLock("resource-a", pool);
        var lockB = _CreateLock("resource-b", pool);

        // when
        var handleA = await lockA.TryAcquireAsync(_Timeout, strategy, contextHandle: null, AbortToken);
        var handleB = await lockB.TryAcquireAsync(_Timeout, strategy, contextHandle: null, AbortToken);

        // then
        handleA.Should().NotBeNull();
        handleB.Should().NotBeNull();
        _connections.Should().ContainSingle("the multiplexing path reuses the pooled connection");

        await handleA!.DisposeAsync();
        await handleB!.DisposeAsync();
    }

    [Fact]
    public async Task should_use_a_dedicated_connection_per_acquire_when_the_strategy_is_upgradeable()
    {
        // given (upgradeable strategies cannot be multiplexed — an elevation could block other locks' release)
        var strategy = new FakeSynchronizationStrategy { IsUpgradeable = true };
        var pool = _CreatePool();

        var lockA = _CreateLock("resource-a", pool);
        var lockB = _CreateLock("resource-b", pool);

        // when
        var handleA = await lockA.TryAcquireAsync(_Timeout, strategy, contextHandle: null, AbortToken);
        var handleB = await lockB.TryAcquireAsync(_Timeout, strategy, contextHandle: null, AbortToken);

        // then (each acquire opened its own dedicated connection; the pool was never used)
        handleA.Should().NotBeNull();
        handleB.Should().NotBeNull();
        _connections.Should().HaveCount(2, "upgradeable acquires bypass multiplexing");

        await handleA!.DisposeAsync();
        await handleB!.DisposeAsync();
    }

    [Fact]
    public async Task should_reuse_the_context_handle_connection_for_a_nested_acquire()
    {
        // given (an upgradeable outer acquire takes a dedicated connection; a nested acquire then reuses it via the
        // context handle — context-handle acquires always flow through the dedicated lock, never the pool)
        var strategy = new FakeSynchronizationStrategy { IsUpgradeable = true };
        var pool = _CreatePool();

        var outerLock = _CreateLock("resource-a", pool);
        var outerHandle = await outerLock.TryAcquireAsync(_Timeout, strategy, contextHandle: null, AbortToken);
        outerHandle.Should().NotBeNull();
        var connectionsAfterOuter = _connections.Count;

        // when (a nested acquire reusing the outer handle's connection)
        var nestedLock = _CreateLock("resource-b", pool);
        var nestedHandle = await nestedLock.TryAcquireAsync(_Timeout, strategy, contextHandle: outerHandle, AbortToken);

        // then (no new connection opened — the nested acquire reused the outer handle's connection rather than the pool)
        nestedHandle.Should().NotBeNull();
        _connections
            .Should()
            .HaveCount(connectionsAfterOuter, "the nested acquire reuses the context handle's connection");

        await nestedHandle!.DisposeAsync();
        await outerHandle!.DisposeAsync();
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

    private OptimisticConnectionMultiplexingDbDistributedLock _CreateLock(
        string resource,
        MultiplexedConnectionLockPool pool
    )
    {
        return new OptimisticConnectionMultiplexingDbDistributedLock(
            resource,
            _ConnectionString,
            pool,
            keepaliveCadence: Timeout.InfiniteTimeSpan
        );
    }
}
