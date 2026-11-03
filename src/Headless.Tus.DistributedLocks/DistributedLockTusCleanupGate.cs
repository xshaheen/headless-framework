// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Headless.Tus;

/// <summary>
/// Cleanup gate backed by <see cref="IDistributedLock"/>: the node that takes the lease runs the pass, and the lease
/// expires on its own after the hold window, so a crashed node never blocks cleanup for longer than one interval.
/// </summary>
internal sealed class DistributedLockTusCleanupGate(IDistributedLock distributedLock, string resourcePrefix)
    : ITusExpiredUploadsCleanupGate
{
    // A colon cannot appear in the generated tus file ids that follow the same prefix with a dash, so this key never
    // contends with a file lock.
    private readonly string _resource = $"{resourcePrefix}:expired-uploads-cleanup";

    public async Task<bool> TryClaimPassAsync(TimeSpan holdFor, CancellationToken cancellationToken)
    {
        var lease = await distributedLock
            .TryAcquireAsync(
                _resource,
                new DistributedLockAcquireOptions
                {
                    // Try once: a node that loses the race skips this tick rather than queueing a second sweep.
                    AcquireTimeout = TimeSpan.Zero,
                    // The lease outlives the pass on purpose, so nodes whose ticks fall later in the same interval
                    // skip too; releasing at the end of the pass would let each node sweep in turn.
                    TimeUntilExpires = holdFor,
                    ReleaseOnDispose = false,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (lease is null)
        {
            return false;
        }

        // Disposing stops tracking the handle without releasing the lease; the TTL ends it.
        await lease.DisposeAsync().ConfigureAwait(false);

        return true;
    }
}
