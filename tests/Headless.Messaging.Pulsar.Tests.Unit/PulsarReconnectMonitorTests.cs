// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Pulsar.Client.Api;

namespace Tests;

public sealed class PulsarReconnectMonitorTests : TestBase
{
    private readonly FakeTimeProvider _time = new();
    private readonly IConsumer<byte[]> _consumer = Substitute.For<IConsumer<byte[]>>();
    private long _lastDisconnected;
    private bool _connected = true;
    private int _probes;
    private int _callbacks;
    private readonly System.Collections.Concurrent.ConcurrentQueue<bool> _states = new();

    public PulsarReconnectMonitorTests()
    {
        _consumer
            .LastDisconnectedTimestamp()
            .Returns(_ =>
            {
                Interlocked.Increment(ref _probes);
                return Task.FromResult(Volatile.Read(ref _lastDisconnected));
            });
        _consumer.IsConnected().Returns(_ => Task.FromResult(Volatile.Read(ref _connected)));
    }

    [Fact]
    public async Task should_report_a_recovered_connection_once()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= 1);

        // when
        Volatile.Write(ref _lastDisconnected, 1_000);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _callbacks) == 1);
        var probesAfterCallback = Volatile.Read(ref _probes);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= probesAfterCallback + 3);

        // then
        Volatile.Read(ref _callbacks).Should().Be(1, "one lost connection is reported once");
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_wait_until_the_consumer_is_connected_again()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= 1);

        // when
        Volatile.Write(ref _connected, false);
        Volatile.Write(ref _lastDisconnected, 1_000);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= 4);

        // then
        Volatile.Read(ref _callbacks).Should().Be(0, "a consumer still reconnecting cannot receive yet");

        Volatile.Write(ref _connected, true);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _callbacks) == 1);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_not_report_a_disconnect_that_preceded_the_monitor()
    {
        // given
        Volatile.Write(ref _lastDisconnected, 500);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);

        // when
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= 4);

        // then
        Volatile.Read(ref _callbacks).Should().Be(0);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_keep_watching_after_a_failed_probe()
    {
        // given
        var errors = 0;
        var failNext = 1;
        _consumer
            .LastDisconnectedTimestamp()
            .Returns(_ =>
            {
                Interlocked.Increment(ref _probes);
                return Interlocked.Exchange(ref failNext, 0) == 1
                    ? Task.FromException<long>(new InvalidOperationException("probe failed"))
                    : Task.FromResult(Volatile.Read(ref _lastDisconnected));
            });

        // Configuring the call above ran the constructor's stub once.
        Volatile.Write(ref _probes, 0);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = PulsarReconnectMonitor.RunAsync(
            _consumer,
            _ =>
            {
                Interlocked.Increment(ref _callbacks);
                return Task.CompletedTask;
            },
            _states.Enqueue,
            _ => Interlocked.Increment(ref errors),
            _time,
            cts.Token
        );
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= 2);

        // when
        Volatile.Write(ref _lastDisconnected, 1_000);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _callbacks) == 1);

        // then
        Volatile.Read(ref errors).Should().Be(1);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_the_connection_state_on_every_probe()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);
        await _AdvanceUntilAsync(() => !_states.IsEmpty);

        // when the broker goes away for a few probes and comes back
        Volatile.Write(ref _connected, false);
        await _AdvanceUntilAsync(() => _states.Where(static connected => !connected).Skip(2).Any());
        Volatile.Write(ref _connected, true);
        var probesBeforeRecovery = _states.Count;
        await _AdvanceUntilAsync(() => _states.Count > probesBeforeRecovery);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);

        // then the probes read connected, then lost, then connected again
        _states.Should().StartWith(true).And.EndWith(true).And.Contain(false);
    }

    [Fact]
    public async Task should_report_the_connection_state_without_a_recovery_callback()
    {
        // given a subscription that loses nothing while away
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = PulsarReconnectMonitor.RunAsync(
            _consumer,
            onReestablished: null,
            _states.Enqueue,
            e => throw new InvalidOperationException("The monitor reported an unexpected error.", e),
            _time,
            cts.Token
        );
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= 1);

        // when it loses and recovers its connection
        Volatile.Write(ref _connected, false);
        Volatile.Write(ref _lastDisconnected, 1_000);
        await _AdvanceUntilAsync(() => _states.Contains(false));
        Volatile.Write(ref _connected, true);
        var probesBeforeRecovery = Volatile.Read(ref _probes);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= probesBeforeRecovery + 2);
        await cts.CancelAsync();
        await monitor.WaitAsync(AbortToken);

        // then the state still reaches the host
        _states.Should().Contain(false).And.EndWith(true);
    }

    [Fact]
    public async Task should_stop_when_canceled()
    {
        // given
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _probes) >= 1);

        // when
        await cts.CancelAsync();

        // then
        await monitor.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        monitor.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_when_canceled_while_a_probe_never_answers()
    {
        // given: the consumer's mailbox stalls, as it can after a broker restart, so a probe never completes
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverAnswered = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumer
            .LastDisconnectedTimestamp()
            .Returns(_ =>
            {
                probeStarted.TrySetResult();
                return neverAnswered.Task;
            });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var monitor = _RunAsync(cts.Token);
        await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await cts.CancelAsync();

        // then
        await monitor.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        monitor.IsCompletedSuccessfully.Should().BeTrue();
    }

    private Task _RunAsync(CancellationToken cancellationToken) =>
        PulsarReconnectMonitor.RunAsync(
            _consumer,
            _ =>
            {
                Interlocked.Increment(ref _callbacks);
                return Task.CompletedTask;
            },
            _states.Enqueue,
            e => throw new InvalidOperationException("The monitor reported an unexpected error.", e),
            _time,
            cancellationToken
        );

    // The monitor parks on a fake-clock delay between probes; advancing until the condition holds avoids racing the
    // moment it registers that delay.
    private async Task _AdvanceUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            _time.Advance(PulsarReconnectMonitor.ProbeInterval);
            await Task.Delay(5, timeout.Token);
        }
    }
}
