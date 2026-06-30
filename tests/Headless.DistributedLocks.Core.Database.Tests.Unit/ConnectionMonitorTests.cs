// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests;

public sealed class ConnectionMonitorTests : TestBase
{
    private const int _CommandTimeoutSeconds = 7;
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_start_monitoring_and_probe_the_connection_when_a_monitoring_handle_is_registered()
    {
        // given
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        // when (registering a handle moves Idle -> Active and starts the worker)
        using var handle = connection.GetConnectionMonitoringHandle();

        // then (the active worker runs the monitoring probe against the connection)
        await _DrainUntilAsync(() => fake.ExecuteNonQueryCount > 0);
        fake.ExecuteNonQueryCount.Should().BePositive();
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_an_already_cancelled_handle_when_the_connection_is_already_closed()
    {
        // given
        await using var fake = new FakeDbConnection();
        fake.SetState(ConnectionState.Closed);
        await using var connection = new TestDatabaseConnection(fake, _timeProvider, _CommandTimeoutSeconds);

        // when
        using var handle = connection.GetConnectionMonitoringHandle();

        // then
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_cancel_every_registered_handle_when_the_connection_transitions_open_to_closed()
    {
        // given
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        using var handleA = connection.GetConnectionMonitoringHandle();
        using var handleB = connection.GetConnectionMonitoringHandle();
        using var handleC = connection.GetConnectionMonitoringHandle();

        handleA.ConnectionLostToken.IsCancellationRequested.Should().BeFalse();

        // when (the connection dies)
        fake.SetState(ConnectionState.Closed);

        // then (every outstanding handle's token is cancelled — the cancel runs on background tasks)
        await _DrainUntilAsync(() =>
            handleA.ConnectionLostToken.IsCancellationRequested
            && handleB.ConnectionLostToken.IsCancellationRequested
            && handleC.ConnectionLostToken.IsCancellationRequested
        );

        handleA.ConnectionLostToken.IsCancellationRequested.Should().BeTrue();
        handleB.ConnectionLostToken.IsCancellationRequested.Should().BeTrue();
        handleC.ConnectionLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_run_a_keepalive_probe_at_the_configured_cadence()
    {
        // given
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        var cadence = TimeSpan.FromSeconds(30);
        connection.SetKeepaliveCadence(cadence);

        // then (the worker is now asleep on the keepalive delay; no probe yet)
        await _DrainUntilAsync(() => false, iterations: 50);
        fake.ExecuteNonQueryCount.Should().Be(0);

        // when (the cadence elapses)
        _timeProvider.Advance(cadence);

        // then (a single keepalive probe runs)
        await _DrainUntilAsync(() => fake.ExecuteNonQueryCount >= 1);
        fake.ExecuteNonQueryCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_apply_a_bounded_command_timeout_to_the_monitoring_probe()
    {
        // given (capture the command timeout the monitor sets on its probe)
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        var observedTimeout = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.ExecuteNonQueryHandler = (command, _) =>
        {
            observedTimeout.TrySetResult(command.CommandTimeout);

            return Task.FromResult(0);
        };

        // when (registering a handle drives the monitoring probe)
        using var handle = connection.GetConnectionMonitoringHandle();

        // then (the probe carries the configured bounded timeout so a silent half-open connection cannot hang it)
        var timeout = await observedTimeout.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        timeout.Should().Be(_CommandTimeoutSeconds);
    }

    [Fact]
    public async Task should_unsubscribe_and_stop_the_worker_without_throwing_when_disposed()
    {
        // given
        var (connection, fake) = _CreateConnection();
        using var handle = connection.GetConnectionMonitoringHandle();
        await _DrainUntilAsync(() => fake.ExecuteNonQueryCount > 0);

        // when
        await connection.DisposeAsync();
        var countAfterDispose = fake.ExecuteNonQueryCount;

        // give any in-flight probe a chance to settle, then confirm the worker stopped
        await _DrainUntilAsync(() => false, iterations: 50);

        // then (no new probes run after dispose; a subsequent state change does not re-cancel/throw)
        fake.ExecuteNonQueryCount.Should().BeLessThanOrEqualTo(countAfterDispose + 1);

        var act = () => fake.SetState(ConnectionState.Closed);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_wake_an_in_flight_monitoring_probe_to_let_a_contended_acquirer_take_the_connection_lock()
    {
        // given (a monitoring probe is in flight and holding the connection lock until it is cancelled — this models the
        // long server-side monitoring sleep that the FIFO-with-retry AcquireConnectionLockAsync path must interrupt)
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        var probeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeObservedCancellation = false;

        fake.ExecuteNonQueryHandler = async (_, cancellationToken) =>
        {
            probeEntered.TrySetResult();

            try
            {
                // Hold the connection lock (as a real server-side sleep would) until the contended acquirer cancels us.
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                probeObservedCancellation = true;
                throw;
            }

            return 0;
        };

        using var handle = connection.GetConnectionMonitoringHandle();
        await probeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when (a caller needs the connection while the probe holds the lock; AcquireConnectionLockAsync must fire
        // state-changed to cancel the in-flight probe and let this acquirer in, retrying until it wins the lock)
        var releaser = await connection
            .ConnectionMonitor.AcquireConnectionLockAsync(AbortToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then (the acquirer got the lock by waking/cancelling the in-flight probe — not by waiting out a timeout)
        releaser.Should().NotBeNull();
        probeObservedCancellation.Should().BeTrue("the contended acquire must cancel the in-flight monitoring probe");

        releaser!.Dispose();
    }

    [Fact]
    public async Task keepalive_probe_should_not_cancel_token_on_non_terminal_command_failure()
    {
        // given
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        // register handle to get connection lost token
        using var handle = connection.GetConnectionMonitoringHandle();

        var cadence = TimeSpan.FromSeconds(30);
        connection.SetKeepaliveCadence(cadence);

        // simulate command failure
        fake.ExecuteNonQueryHandler = (_, _) => throw new InvalidOperationException("Non-terminal database error");

        // when (advancing past cadence)
        _timeProvider.Advance(cadence);

        // then (probe ran and failed, but since connection state remains Open, token is NOT cancelled)
        await _DrainUntilAsync(() => fake.ExecuteNonQueryCount >= 1);
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeFalse();

        // when (terminal loss occurs and connection state is set to Closed)
        fake.SetState(ConnectionState.Closed);

        // then (the token is cancelled)
        await _DrainUntilAsync(() => handle.ConnectionLostToken.IsCancellationRequested);
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_fire_a_handle_registered_after_reactivation_when_the_monitor_is_cleanly_stopped()
    {
        // Regression for #403: the contested worker-start race (Active flip at _StartMonitorWorkerIfNeededNoLock vs. the
        // worker continuation actually running). Drives the AutoStopped -> reactivate -> register-handle -> StopAsync
        // interleaving and asserts the registered handle's token is NOT fired. A clean StopAsync is proper disposal, not
        // connection loss: the only path that may fire ConnectionLostToken is a real Open -> closed state change (proven
        // by the sibling _on_connection_loss test below), so there is no firing obligation to strand here.

        // given (Idle)
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        // when (Idle -> AutoStopped on connection loss, then AutoStopped -> Idle on reactivation)
        fake.SetState(ConnectionState.Closed);
        fake.SetState(ConnectionState.Open);

        // register a handle in the reactivated window: Idle -> Active, starting the worker
        using var handle = connection.GetConnectionMonitoringHandle();
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeFalse();

        // close the worker-start window deterministically: wait until the worker has actually issued a probe
        await _DrainUntilAsync(() => fake.ExecuteNonQueryCount > 0);

        // a clean stop tears down registrations without cancelling them (isCancel: false)
        await connection.ConnectionMonitor.StopAsync();

        // then (the token never fired — clean stop is not a connection loss; give any racing cancel a chance to surface)
        await _DrainUntilAsync(() => false, iterations: 50);
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_fire_a_handle_registered_after_reactivation_when_the_connection_is_actually_lost()
    {
        // Sibling to the clean-stop regression above: the same AutoStopped -> reactivate -> register-handle window, but
        // here the connection is genuinely lost (Open -> closed) instead of cleanly stopped. This proves the reactivated
        // worker still wires the handle to the real connection-loss path, so a handle from that window is never stranded.

        // given (Idle)
        var (connection, fake) = _CreateConnection();
        await using var _ = connection;

        // when (Idle -> AutoStopped -> Idle reactivation, then register in the reactivated window: Idle -> Active)
        fake.SetState(ConnectionState.Closed);
        fake.SetState(ConnectionState.Open);

        using var handle = connection.GetConnectionMonitoringHandle();
        await _DrainUntilAsync(() => fake.ExecuteNonQueryCount > 0);
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeFalse();

        // the connection actually dies
        fake.SetState(ConnectionState.Closed);

        // then (the handle's token is cancelled — the reactivated worker honored the real connection-loss path)
        await _DrainUntilAsync(() => handle.ConnectionLostToken.IsCancellationRequested);
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeTrue();
    }

    private (TestDatabaseConnection Connection, FakeDbConnection Fake) _CreateConnection()
    {
        var fake = new FakeDbConnection();
        var connection = new TestDatabaseConnection(fake, _timeProvider, _CommandTimeoutSeconds);

        return (connection, fake);
    }

    private static async Task _DrainUntilAsync(Func<bool> condition, int iterations = 2000)
    {
        for (var i = 0; i < iterations && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1), AbortToken);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
