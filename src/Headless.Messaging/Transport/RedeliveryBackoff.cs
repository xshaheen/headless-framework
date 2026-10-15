// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Threading;

namespace Headless.Messaging.Transport;

/// <summary>
/// The delays a delivery the core could not take in waits before the broker hands it out again, and the wait itself.
/// </summary>
/// <remarks>
/// A broker counts every redelivery against its limit: Azure Service Bus dead-letters at <c>MaxDeliveryCount</c>, a
/// RabbitMQ quorum queue at its delivery limit, and Kafka seeks straight back to the record. Rejecting a delivery the
/// moment its store write fails therefore spends that whole budget inside a short database blip and dead-letters a
/// message no consumer ever saw. The delays run in real time: they wait for a dependency to come back, which an app
/// clock that a test host fakes would never let happen.
/// </remarks>
internal static class RedeliveryBackoff
{
    /// <summary>The retries of a failed inbox admission write before the delivery is handed back to the broker.</summary>
    public const int AdmissionRetries = 3;

    // 250 ms doubling over three retries stays under two seconds in total, far inside every broker's lock or visibility
    // window, so the in-process retry never costs the delivery its lock.
    private static readonly TimeSpan _FirstAdmissionRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>The jittered wait before admission retry <paramref name="retry"/> (1-based): about 250 ms, doubling.</summary>
    public static TimeSpan AdmissionRetryDelay(int retry, Random random)
    {
        var doublings = Math.Clamp(retry - 1, 0, AdmissionRetries - 1);
        return ReconnectBackoff.Jitter(
            TimeSpan.FromTicks(_FirstAdmissionRetryDelay.Ticks << doublings),
            TimeSpan.Zero,
            random
        );
    }

    /// <summary>
    /// The jittered delay before the broker redelivers a delivery after <paramref name="failures"/> consecutive failed
    /// attempts (1-based): about one second, doubling up to <see cref="ReconnectBackoff.Ceiling"/>.
    /// </summary>
    public static TimeSpan RedeliveryDelay(long failures, Random random)
    {
        // Five doublings from one second already pass the 30 s ceiling.
        var doublings = (int)Math.Clamp(failures - 1, 0, 5);
        return ReconnectBackoff.Jitter(
            TimeSpan.FromTicks(ReconnectBackoff.FirstDelay.Ticks << doublings),
            TimeSpan.Zero,
            random
        );
    }

    /// <summary>
    /// Holds the delivery for <paramref name="delay"/>, then rejects it. A cancelled <paramref name="cancellationToken"/>
    /// ends the wait early, so a stopping host does not wait out the delay; the reject still runs, because settlement
    /// is must-complete.
    /// </summary>
    public static async ValueTask DelayThenRejectAsync(
        IConsumerClient client,
        object? sender,
        TimeSpan delay,
        TimeProvider clock,
        CancellationToken cancellationToken
    )
    {
        if (delay > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, clock, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopping: hand the delivery back now rather than hold it through shutdown.
            }
        }

        await client.RejectAsync(sender, CancellationToken.None).ConfigureAwait(false);
    }
}
