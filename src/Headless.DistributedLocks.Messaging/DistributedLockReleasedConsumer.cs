// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

/// <summary>
/// Messaging consumer that fans a <see cref="DistributedLockReleased"/> event out to every registered
/// <see cref="IDistributedLockReleaseListener"/> (mutex, semaphore, and reader-writer), allowing blocked acquirers to be
/// woken immediately after a release rather than waiting for the next backoff poll interval.
/// </summary>
/// <remarks>
/// Every-instance: the waiters live in each process, so every process must see every release instead of sharing one
/// copy with its replicas. Delivery is at most once and needs no reconnect hook, because a waiter that misses a
/// release still wakes on its next backoff poll.
/// </remarks>
[BusConsumer(Identity, EveryInstance = true)]
internal sealed class DistributedLockReleasedConsumer(
    IEnumerable<IDistributedLockReleaseListener> listeners,
    ILogger<DistributedLockReleasedConsumer> logger
) : IConsume<DistributedLockReleased>
{
    /// <summary>The consumer identity that <c>Tune</c> and <c>ConsumeOnly</c> refer to.</summary>
    public const string Identity = "headless.distributed-locks.release";

    /// <summary>
    /// Fans the release signal to all registered listeners and returns synchronously. If
    /// <paramref name="cancellationToken"/> is already cancelled, returns a cancelled <see cref="ValueTask"/> without
    /// calling any listener.
    /// </summary>
    /// <param name="context">The consume context carrying the <see cref="DistributedLockReleased"/> message.</param>
    /// <param name="cancellationToken">Token cancelled on application shutdown.</param>
    /// <returns>A completed (or cancelled) <see cref="ValueTask"/>.</returns>
    public ValueTask ConsumeAsync(ConsumeContext<DistributedLockReleased> context, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        logger.LogProcessingLockReleased(context.MessageId, context.Message.Resource);

        foreach (var listener in listeners)
        {
            listener.OnReleased(context.Message.Resource, context.Message.LeaseId);
        }

        return ValueTask.CompletedTask;
    }
}
