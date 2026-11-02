// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarReconnectMonitorTests : TestBase
{
    private readonly FakePulsarReceiver _receiver = new("headless-bus-orders");
    private readonly ConcurrentQueue<bool> _states = new();
    private readonly ConcurrentQueue<Exception> _errors = new();
    private int _callbacks;

    [Fact]
    public async Task should_report_a_recovered_connection_once()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);

        // when
        _receiver.SetLink(PulsarLinkState.Disconnected);
        _receiver.SetLink(PulsarLinkState.Connected);
        await _WaitUntilAsync(() => Volatile.Read(ref _callbacks) == 1);
        await Task.Delay(100, AbortToken);

        // then
        Volatile.Read(ref _callbacks).Should().Be(1, "one lost connection is reported once");
        _states.Should().Equal(false, true);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_wait_until_the_receiver_is_connected_again()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);

        // when
        _receiver.SetLink(PulsarLinkState.Disconnected);
        await _WaitUntilAsync(() => _states.Count == 1);
        await Task.Delay(100, AbortToken);

        // then
        Volatile.Read(ref _callbacks).Should().Be(0, "the subscription cannot receive until it reconnects");
        _receiver.SetLink(PulsarLinkState.Connected);
        await _WaitUntilAsync(() => Volatile.Read(ref _callbacks) == 1);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_a_receiver_still_reconnecting_again_until_it_is_back()
    {
        // given: the host calls the broker unreachable only after several failures in a row
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = PulsarReconnectMonitor.RunAsync(
            _receiver,
            null,
            _states.Enqueue,
            _errors.Enqueue,
            cts.Token,
            disconnectedReportInterval: TimeSpan.FromMilliseconds(20)
        );

        // when
        _receiver.SetLink(PulsarLinkState.Disconnected);
        await _WaitUntilAsync(() => _states.Count >= 3);
        _receiver.SetLink(PulsarLinkState.Connected);
        await _WaitUntilAsync(() => _states.Contains(true));

        // then
        _states.TakeWhile(static connected => !connected).Should().HaveCountGreaterThanOrEqualTo(3);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_the_connection_state_without_a_recovery_callback()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = PulsarReconnectMonitor.RunAsync(_receiver, null, _states.Enqueue, _errors.Enqueue, cts.Token);

        // when
        _receiver.SetLink(PulsarLinkState.Disconnected);
        _receiver.SetLink(PulsarLinkState.Connected);
        await _WaitUntilAsync(() => _states.Count == 2);

        // then
        _states.Should().Equal(false, true);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_keep_watching_after_a_failed_callback()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = PulsarReconnectMonitor.RunAsync(
            _receiver,
            _ =>
            {
                Interlocked.Increment(ref _callbacks);
                throw new InvalidOperationException("hook failed");
            },
            _states.Enqueue,
            _errors.Enqueue,
            cts.Token
        );

        // when
        _receiver.SetLink(PulsarLinkState.Disconnected);
        _receiver.SetLink(PulsarLinkState.Connected);
        _receiver.SetLink(PulsarLinkState.Disconnected);
        _receiver.SetLink(PulsarLinkState.Connected);
        await _WaitUntilAsync(() => Volatile.Read(ref _callbacks) == 2);

        // then
        _errors.Should().HaveCount(2).And.AllSatisfy(e => e.Message.Should().Be("hook failed"));
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_stop_when_the_receiver_ends()
    {
        // given
        var monitor = _RunAsync(AbortToken);

        // when
        _receiver.SetLink(PulsarLinkState.Ended);

        // then
        await monitor.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        _states.Should().Equal(false);
        Volatile.Read(ref _callbacks).Should().Be(0);
    }

    [Fact]
    public async Task should_stop_when_canceled()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);

        // when
        await cts.CancelAsync();

        // then
        await monitor.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        _errors.Should().BeEmpty();
    }

    private Task _RunAsync(CancellationToken cancellationToken)
    {
        return PulsarReconnectMonitor.RunAsync(
            _receiver,
            _ =>
            {
                Interlocked.Increment(ref _callbacks);
                return Task.CompletedTask;
            },
            _states.Enqueue,
            _errors.Enqueue,
            cancellationToken
        );
    }

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            await Task.Delay(10, deadline.Token);
        }
    }
}
