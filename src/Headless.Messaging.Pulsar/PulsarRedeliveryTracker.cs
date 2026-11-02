// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Redelivers rejected messages after the negative-acknowledgement delay, and messages left unacknowledged past the
/// ack timeout, moving each message past its redelivery limit to the dead-letter topic instead.
/// </summary>
/// <remarks>
/// The broker has no delayed negative acknowledgement, ack timeout, or dead-letter policy of its own: the Apache Pulsar
/// clients implement all three on the client by asking the broker to redeliver chosen messages, and DotPulsar leaves
/// that to its caller. The broker counts each redelivery, so a message's redelivery count is what the dead-letter limit
/// reads. A message is never dropped here: one whose dead-letter publish fails is redelivered instead.
/// </remarks>
internal sealed class PulsarRedeliveryTracker
{
    private static readonly TimeSpan _MinTick = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan _MaxTick = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<PulsarDelivery, long> _due = new(ReferenceEqualityComparer.Instance);
    private readonly TimeSpan _negativeAckDelay;
    private readonly TimeSpan? _ackTimeout;
    private readonly int? _maxRedeliveryCount;
    private readonly Func<PulsarDelivery, CancellationToken, Task<bool>> _deadLetterAsync;
    private readonly Action<Exception> _onError;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a tracker for the messages of one consumer client.</summary>
    /// <param name="negativeAckDelay">How long a rejected message waits before it is redelivered.</param>
    /// <param name="ackTimeout">How long a received message may stay unacknowledged, or <see langword="null"/> for no limit.</param>
    /// <param name="maxRedeliveryCount">
    /// The redelivery count at which a message goes to the dead-letter topic instead, or <see langword="null"/> for none.
    /// </param>
    /// <param name="deadLetterAsync">
    /// Publishes a message to its dead-letter topic and acknowledges it; returns <see langword="false"/> when it could
    /// not, so the message is redelivered instead.
    /// </param>
    /// <param name="onError">Receives a failed redelivery request; the broker still redelivers when the consumer closes.</param>
    /// <param name="timeProvider">
    /// The clock that times the delays. A redelivery delay waits real time, so production passes the system clock.
    /// </param>
    public PulsarRedeliveryTracker(
        TimeSpan negativeAckDelay,
        TimeSpan? ackTimeout,
        int? maxRedeliveryCount,
        Func<PulsarDelivery, CancellationToken, Task<bool>> deadLetterAsync,
        Action<Exception> onError,
        TimeProvider timeProvider
    )
    {
        _negativeAckDelay = negativeAckDelay;
        _ackTimeout = ackTimeout;
        _maxRedeliveryCount = maxRedeliveryCount;
        _deadLetterAsync = deadLetterAsync;
        _onError = onError;
        _timeProvider = timeProvider;

        var shortest = ackTimeout is { } timeout && timeout < negativeAckDelay ? timeout : negativeAckDelay;
        Tick = TimeSpan.FromTicks(Math.Clamp(shortest.Ticks / 10, _MinTick.Ticks, _MaxTick.Ticks));
    }

    /// <summary>Gets how often due messages are collected.</summary>
    public TimeSpan Tick { get; }

    /// <summary>Gets how many messages wait for a redelivery or an ack timeout.</summary>
    public int Count => _due.Count;

    /// <summary>Starts the ack timeout of a received message, when the consumer has one.</summary>
    public void OnReceived(PulsarDelivery delivery)
    {
        if (_ackTimeout is { } timeout && delivery.Receiver.Settles)
        {
            _due[delivery] = _timeProvider.GetTimestamp() + _ToTimestampTicks(timeout);
        }
    }

    /// <summary>Forgets an acknowledged message.</summary>
    public void OnAcknowledged(PulsarDelivery delivery)
    {
        _due.TryRemove(delivery, out _);
    }

    /// <summary>Schedules a rejected message for redelivery after the negative-acknowledgement delay.</summary>
    public void OnRejected(PulsarDelivery delivery)
    {
        if (delivery.Receiver.Settles)
        {
            _due[delivery] = _timeProvider.GetTimestamp() + _ToTimestampTicks(_negativeAckDelay);
        }
    }

    /// <summary>Redelivers due messages every <see cref="Tick"/> until <paramref name="cancellationToken"/> is canceled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _timeProvider.Delay(Tick, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RedeliverDueAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Redelivers, or dead-letters, every message whose delay has passed.</summary>
    public async Task RedeliverDueAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetTimestamp();
        List<PulsarDelivery>? due = null;

        foreach (var (delivery, dueAt) in _due)
        {
            if (dueAt <= now && _due.TryRemove(new KeyValuePair<PulsarDelivery, long>(delivery, dueAt)))
            {
                (due ??= []).Add(delivery);
            }
        }

        if (due is null)
        {
            return;
        }

        List<PulsarDelivery>? redeliver = null;
        foreach (var delivery in due)
        {
            if (
                _maxRedeliveryCount is { } max
                && delivery.Message.RedeliveryCount >= max
                && await _deadLetterAsync(delivery, cancellationToken).ConfigureAwait(false)
            )
            {
                continue;
            }

            (redeliver ??= []).Add(delivery);
        }

        if (redeliver is null)
        {
            return;
        }

        foreach (var group in redeliver.GroupBy(static delivery => delivery.Receiver))
        {
            try
            {
                await group
                    .Key.RedeliverAsync(
                        group.Select(static delivery => delivery.MessageId).ToArray(),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // The messages stay unacknowledged, so the broker redelivers them once this consumer closes.
                _onError(e);
            }
        }
    }

    private long _ToTimestampTicks(TimeSpan delay) =>
        (long)(delay.Ticks * (_timeProvider.TimestampFrequency / (double)TimeSpan.TicksPerSecond));
}
