// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// The message status name.
/// </summary>
public enum StatusName
{
    Failed = -1,
    Scheduled,
    Succeeded,

    Delayed,
    Queued,
}
