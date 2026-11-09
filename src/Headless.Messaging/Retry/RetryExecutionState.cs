// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

internal sealed class RetryExecutionState
{
    public bool LeaseClearedByTransition { get; private set; }

    public void RecordLeaseTransition(bool affected, bool keepLease)
    {
        if (affected)
        {
            LeaseClearedByTransition = !keepLease;
        }
    }
}
