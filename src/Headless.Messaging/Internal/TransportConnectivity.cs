// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Whether the host can reach its broker, judged from the receive and send outcomes the host already observes rather
/// than from a probe of its own.
/// </summary>
/// <remarks>
/// Consumer clients and the outbox sender report into one state, so a publish-only host and a consuming host answer
/// the same way. Only a success clears a failure run: the consumer register's restart flag is reset at the start of
/// every rebuild, before any client has reached the broker, so it says nothing about the broker.
/// </remarks>
internal sealed class TransportConnectivity
{
    /// <summary>
    /// The number of broker failures in a row, with no success between them, that marks the broker unreachable. One
    /// failed receive or send is often a blip the client retries through, and readiness should not follow it.
    /// </summary>
    internal const int FailureThreshold = 3;

    private int _consecutiveFailures;
    private long _failuresReported;

    /// <summary>
    /// Gets whether the broker counts as reachable: fewer than <see cref="FailureThreshold"/> failures have been
    /// reported since the last success.
    /// </summary>
    public bool IsReachable => Volatile.Read(ref _consecutiveFailures) < FailureThreshold;

    /// <summary>
    /// Gets a mark that moves on every reported failure, so <see cref="ReportEstablished"/> can tell whether the broker
    /// failed while a consumer client was starting.
    /// </summary>
    public long FailureMark => Interlocked.Read(ref _failuresReported);

    /// <summary>Records a receive, send, or connection attempt that failed because the broker could not be reached.</summary>
    public void ReportBrokerFailure()
    {
        Interlocked.Increment(ref _failuresReported);

        // Saturates at the threshold: a long outage must not overflow the count, and past it the value means the same.
        var current = Volatile.Read(ref _consecutiveFailures);
        while (current < FailureThreshold)
        {
            var observed = Interlocked.CompareExchange(ref _consecutiveFailures, current + 1, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    /// <summary>
    /// Records a consumer client that reported itself ready, as a success unless a failure was reported since
    /// <paramref name="markAtStart"/>.
    /// </summary>
    /// <remarks>
    /// Not every client needs the broker to become ready: a Kafka consumer is ready once its first poll returns, which
    /// it does on an empty timeout while the broker is down. The connection errors it reports while starting are what
    /// keep such a start from reading as a recovery.
    /// </remarks>
    /// <param name="markAtStart">The <see cref="FailureMark"/> read before the client was created.</param>
    public void ReportEstablished(long markAtStart)
    {
        if (FailureMark == markAtStart)
        {
            ReportBrokerSuccess();
        }
    }

    /// <summary>Records an exchange with the broker that succeeded, which ends any failure run.</summary>
    public void ReportBrokerSuccess()
    {
        // Every successful send lands here; reading first keeps the shared cache line clean on the hot path.
        if (Volatile.Read(ref _consecutiveFailures) != 0)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }
    }
}
