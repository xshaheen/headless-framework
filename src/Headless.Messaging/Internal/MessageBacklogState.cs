// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// The one in-code definition of a backlog row, shared by the storages that count it in memory; the relational
/// monitoring API states the same rule in SQL. See <see cref="BacklogView"/> for the rule itself.
/// </summary>
internal static class MessageBacklogState
{
    /// <summary>
    /// When <paramref name="status"/> makes a row pending, the instant it became eligible for dispatch; otherwise
    /// <see langword="null"/>. A delayed or queued row's <paramref name="expiresAt"/> holds its publish time, so it is
    /// pending only once that time has come, and waits from then.
    /// </summary>
    public static DateTimeOffset? PendingSince(
        StatusName status,
        DateTimeOffset added,
        DateTimeOffset? expiresAt,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset now
    )
    {
        return status switch
        {
            StatusName.Scheduled => added,
            StatusName.Delayed or StatusName.Queued when expiresAt is null => added,
            StatusName.Delayed or StatusName.Queued when expiresAt <= now => expiresAt,
            StatusName.Failed when nextRetryAt is not null => added,
            _ => null,
        };
    }
}
