// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

internal sealed class RetryExecutionState
{
    public bool LeaseClearedByTransition { get; private set; }

    /// <summary>
    /// Whether the attempt runs on the retry pickup's own task, which dispatches a claimed batch one row at a time. A
    /// wait there holds every row behind it, so such an attempt puts its row back instead of waiting for a rate-limit
    /// permit.
    /// </summary>
    public bool RunsOnPickupTask { get; init; }

    public void RecordLeaseTransition(bool affected, bool keepLease)
    {
        if (affected)
        {
            LeaseClearedByTransition = !keepLease;
        }
    }
}
