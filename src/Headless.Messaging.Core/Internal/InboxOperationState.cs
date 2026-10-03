// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Internal;

internal readonly record struct InboxOperationState(
    StatusName Status,
    bool HasNextRetry,
    bool IsHeld,
    bool IsCurrentGeneration,
    long Generation,
    bool IsOrphaned = false,
    bool HasLiveClaim = false
);
