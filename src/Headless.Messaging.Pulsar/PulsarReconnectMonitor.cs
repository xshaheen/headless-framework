// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Watches a Pulsar consumer for connections it lost and recovered on its own, which Pulsar.Client does without telling
/// its caller.
/// </summary>
/// <remarks>
/// Pulsar.Client reconnects a consumer internally and exposes no reconnect event, only the time of the last lost
/// connection and whether the consumer is connected now. A non-durable subscription keeps no cursor on the broker while
/// the consumer is away, so messages published during the gap can be lost; the monitor reports each recovery so the
/// messaging core can tell the consumer.
/// </remarks>
internal static class PulsarReconnectMonitor
{
    /// <summary>How often the monitor reads the consumer's connection state.</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Reports whether <paramref name="consumer"/> is connected on every probe, and invokes
    /// <paramref name="onReestablished"/> once each time it is connected again after it lost its connection, until
    /// <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    /// <param name="consumer">The consumer whose subscription to watch.</param>
    /// <param name="onReestablished">
    /// The callback to invoke after a recovery, or <see langword="null"/> for a subscription that loses nothing while
    /// away.
    /// </param>
    /// <param name="onConnectionState">
    /// Receives the consumer's connection state on each probe. A receive waits quietly through an outage, so this is
    /// how the host learns that the broker went away.
    /// </param>
    /// <param name="onError">Receives a failure to read the consumer state or of the callback; the monitor keeps running.</param>
    /// <param name="timeProvider">The clock that paces the probes.</param>
    /// <param name="cancellationToken">Stops the monitor.</param>
    /// <returns>A task that completes once <paramref name="cancellationToken"/> is canceled.</returns>
    public static async Task RunAsync(
        IConsumer<byte[]> consumer,
        Func<CancellationToken, Task>? onReestablished,
        Action<bool> onConnectionState,
        Action<Exception> onError,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        long? handled = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Each probe goes through the consumer's mailbox, which can stall after a broker restart, so a stop
                // abandons a probe still waiting instead of holding the receive loop that awaits this monitor.
                var lastDisconnected = await consumer
                    .LastDisconnectedTimestamp()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                var connected = await consumer.IsConnected().WaitAsync(cancellationToken).ConfigureAwait(false);
                onConnectionState(connected);

                // The first read sets the baseline: a disconnect before the monitor started preceded the subscription
                // the core already announced. A consumer still reconnecting is checked again on the next probe, so the
                // callback runs only once the recovered subscription can receive.
                if (handled is null)
                {
                    handled = lastDisconnected;
                }
                else if (lastDisconnected != handled && connected)
                {
                    handled = lastDisconnected;
                    if (onReestablished is not null)
                    {
                        await onReestablished(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                onError(e);
            }

            try
            {
                await timeProvider.Delay(ProbeInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
