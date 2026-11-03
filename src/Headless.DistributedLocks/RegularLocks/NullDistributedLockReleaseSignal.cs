// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

/// <summary>
/// The default <see cref="IDistributedLockReleaseSignal"/>: it announces nothing, so waiters wake on their backoff
/// poll. Logs once when constructed so operators see why release wake-ups are not pushed.
/// </summary>
internal sealed class NullDistributedLockReleaseSignal : IDistributedLockReleaseSignal
{
    public NullDistributedLockReleaseSignal(ILogger<NullDistributedLockReleaseSignal> logger)
    {
        logger.LogReleaseSignalAbsent();
    }

    public void Announce(string resource, string leaseId) { }
}
