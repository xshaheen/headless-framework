// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NATS.Client.JetStream;

namespace Headless.Messaging.Nats;

/// <summary>
/// The settlement token of one JetStream delivery. While the delivery waits for a handler slot and runs, it tells the
/// server every half <c>AckWait</c> that work is in progress, so a slow receive stage does not get a duplicate
/// concurrent delivery when <c>AckWait</c> runs out. Settling the delivery stops the loop first.
/// </summary>
internal sealed class NatsJSDelivery : IAsyncDisposable
{
    private readonly CancellationTokenSource _progressCts = new();
    private readonly Task _progress;
    private int _stopped;

    /// <summary>Starts reporting <paramref name="msg"/> in progress.</summary>
    /// <param name="msg">The JetStream delivery.</param>
    /// <param name="ackWait">
    /// The consumer's <c>AckWait</c>; <see cref="TimeSpan.Zero"/> or less sends no progress, for a consumer that leaves
    /// the server default in place.
    /// </param>
    /// <param name="clock">The clock the progress interval runs on.</param>
    /// <param name="onProgressFailure">Reports a progress signal the server did not take; the loop keeps going.</param>
    public NatsJSDelivery(
        INatsJSMsg<ReadOnlyMemory<byte>> msg,
        TimeSpan ackWait,
        TimeProvider clock,
        Action<Exception> onProgressFailure
    )
    {
        Msg = msg;
        _progress =
            ackWait > TimeSpan.Zero
                ? _KeepInProgressAsync(msg, ackWait / 2, clock, onProgressFailure, _progressCts.Token)
                : Task.CompletedTask;
    }

    public INatsJSMsg<ReadOnlyMemory<byte>> Msg { get; }

    /// <summary>Stops the progress loop and waits for a signal in flight, so none reaches the server after settlement.</summary>
    public async Task StopProgressAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            await _progressCts.CancelAsync().ConfigureAwait(false);
        }

        await _progress.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopProgressAsync().ConfigureAwait(false);
        _progressCts.Dispose();
    }

    private static async Task _KeepInProgressAsync(
        INatsJSMsg<ReadOnlyMemory<byte>> msg,
        TimeSpan interval,
        TimeProvider clock,
        Action<Exception> onProgressFailure,
        CancellationToken cancellationToken
    )
    {
        // Yield first: the constructor must not run the loop inline on the delivery's receive path.
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, clock, cancellationToken).ConfigureAwait(false);
                await msg.AckProgressAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A lost signal only risks a redelivery JetStream would make anyway; the next tick tries again.
                onProgressFailure(ex);
            }
        }
    }
}
