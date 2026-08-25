// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

internal sealed class RetryExecutionState
{
    public bool LeaseClearedByTransition { get; private set; }

    public void RecordLeaseTransition(bool affected, DateTimeOffset? lockedUntil)
    {
        if (affected)
        {
            LeaseClearedByTransition = lockedUntil is null;
        }
    }
}
