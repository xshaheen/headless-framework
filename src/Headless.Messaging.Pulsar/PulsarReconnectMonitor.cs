// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Watches a receiver for connections it lost and recovered on its own, which DotPulsar does without telling its caller
/// except through the receiver's state.
/// </summary>
/// <remarks>
/// DotPulsar reconnects a consumer or reader internally while a receive waits quietly, so the state is how the host
/// learns that the broker went away. A non-durable subscription keeps no cursor on the broker while the reader is away,
/// so messages published during the gap can be lost; the monitor reports each recovery so the messaging core can tell
/// the consumer.
/// </remarks>
internal static class PulsarReconnectMonitor
{
    /// <summary>
    /// How often a receiver still reconnecting is reported unreachable again. The host counts several failures in a row
    /// before it calls the broker unreachable, so one report per outage would never reach that count.
    /// </summary>
    public static readonly TimeSpan DisconnectedReportInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Reports each change of <paramref name="receiver"/>'s connection, and invokes <paramref name="onReestablished"/>
    /// once each time it is connected again after it lost its connection, until the receiver ends or
    /// <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    /// <param name="receiver">The receiver to watch, already connected.</param>
    /// <param name="onReestablished">
    /// The callback to invoke after a recovery, or <see langword="null"/> for a subscription that loses nothing while
    /// away.
    /// </param>
    /// <param name="onConnectionState">Receives whether the receiver is connected, on each change.</param>
    /// <param name="onError">Receives a failure of the callback or of the state watch; the monitor keeps running.</param>
    /// <param name="cancellationToken">Stops the monitor.</param>
    /// <param name="disconnectedReportInterval">
    /// How often a disconnected receiver is reported again; <see cref="DisconnectedReportInterval"/> unless a test sets
    /// it. The interval waits real time, as a reconnect does.
    /// </param>
    /// <returns>A task that completes once the receiver ended or <paramref name="cancellationToken"/> is canceled.</returns>
    public static async Task RunAsync(
        IPulsarReceiver receiver,
        Func<CancellationToken, Task>? onReestablished,
        Action<bool> onConnectionState,
        Action<Exception> onError,
        CancellationToken cancellationToken,
        TimeSpan? disconnectedReportInterval = null
    )
    {
        var reportInterval = disconnectedReportInterval ?? DisconnectedReportInterval;
        var state = PulsarLinkState.Connected;

        while (!cancellationToken.IsCancellationRequested)
        {
            PulsarLinkState next;
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (state is PulsarLinkState.Disconnected)
            {
                wait.CancelAfter(reportInterval);
            }

            try
            {
                next = await receiver.WaitForLinkChangeAsync(state, wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested)
            {
                // Still reconnecting: report it again, so the host sees a run of failures rather than a single blip.
                onConnectionState(false);
                continue;
            }
            catch (Exception e)
            {
                // A state watch that fails would fail again at once; the receive loop still sees an ended receiver.
                onError(e);
                return;
            }

            var recovered = state is PulsarLinkState.Disconnected && next is PulsarLinkState.Connected;
            state = next;
            onConnectionState(next is PulsarLinkState.Connected);

            if (next is PulsarLinkState.Ended)
            {
                // The receive loop sees the ended receiver too, and fails the listener so the core rebuilds it.
                return;
            }

            if (!recovered || onReestablished is null)
            {
                continue;
            }

            try
            {
                await onReestablished(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                onError(e);
            }
        }
    }
}
