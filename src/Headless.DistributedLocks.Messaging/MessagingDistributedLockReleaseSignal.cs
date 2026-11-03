// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

/// <summary>
/// Sends each confirmed release as a <see cref="DistributedLockReleased"/> message, which
/// <see cref="DistributedLockReleasedConsumer"/> delivers to every host.
/// </summary>
internal sealed class MessagingDistributedLockReleaseSignal(
    IBus bus,
    ILogger<MessagingDistributedLockReleaseSignal> logger
) : IDistributedLockReleaseSignal
{
    // A system message: a lock released from host scope carries no tenant and must still wake peers when the host
    // requires a tenant on every publish.
    internal static PublishOptions PublishOptions { get; } =
        new() { DeliveryMode = DeliveryMode.Direct, IsSystemMessage = true };

    /// <summary>
    /// Sends the release wake-up without making the release wait for the broker. The signal is a best-effort hint
    /// whose loss waiters already absorb with their backoff poll, so awaiting it only added a broker round trip to
    /// every release. A failure, synchronous or later, is logged and never surfaces to the releasing caller.
    /// </summary>
    public void Announce(string resource, string leaseId)
    {
        Task<PublishReceipt> publish;

        try
        {
            // The caller's token is not passed: the publish outlives the call, so a token cancelled or disposed after
            // the release returned must not decide whether the wake-up goes out.
            publish = bus.PublishAsync(
                new DistributedLockReleased(resource, leaseId),
                PublishOptions,
                CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            logger.LogLockReleasePublishFailed(exception, resource, leaseId);
            return;
        }

        if (publish.IsCompleted)
        {
            if (publish.IsFaulted)
            {
                logger.LogLockReleasePublishFailed(publish.Exception.GetBaseException(), resource, leaseId);
            }

            return;
        }

        _ = publish.ContinueWith(
            static (task, state) =>
            {
                var (logger, resource, leaseId) = ((ILogger, string, string))state!;
                logger.LogLockReleasePublishFailed(task.Exception!.GetBaseException(), resource, leaseId);
            },
            // Boxed as ILogger, not ILogger<T>: the continuation unboxes a (ILogger, string, string) tuple, and a
            // tuple of a different element type fails that unbox and loses the log.
            ((ILogger)logger, resource, leaseId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }
}
