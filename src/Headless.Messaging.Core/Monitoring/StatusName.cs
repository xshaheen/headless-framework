// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

/// <summary>
/// The persisted lifecycle status of a stored message. The enum member names are the exact strings
/// written to (and read from) the storage <c>StatusName</c> column and surfaced on the monitoring API,
/// so renaming a member is a storage- and wire-breaking change.
/// </summary>
/// <remarks>
/// Additional members may be added in future versions, so consumers that switch on this enum should
/// include a default branch to handle values they do not recognize.
/// </remarks>
[PublicAPI]
public enum StatusName
{
    /// <summary>Processing failed permanently; the message will not be retried.</summary>
    Failed = -1,

    /// <summary>The message is scheduled for future delivery and has not yet been queued.</summary>
    Scheduled = 0,

    /// <summary>The message was processed successfully.</summary>
    Succeeded = 1,

    /// <summary>The message is delayed and awaiting its next retry/dispatch window.</summary>
    Delayed = 2,

    /// <summary>The message is queued and awaiting processing.</summary>
    Queued = 3,
}
