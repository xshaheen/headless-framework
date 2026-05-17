// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Messages;

[PublicAPI]
public class MediumMessage
{
    public required long StorageId { get; set; }

    public required Message Origin { get; set; }

    public required string Content { get; set; }

    public DateTime Added { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp at which the persisted retry processor should re-dispatch
    /// this message. <see langword="null"/> means no scheduled retry.
    /// </summary>
    /// <remarks>
    /// The value MUST be UTC (<see cref="DateTimeKind.Utc"/>). Use
    /// <see cref="DateTime.UtcNow"/> or <see cref="TimeProvider.GetUtcNow"/>.UtcDateTime —
    /// never the local-clock <c>DateTime.Now</c>. Storage providers (Npgsql in particular)
    /// reject non-UTC <see cref="DateTime"/> values when writing to <c>timestamptz</c> columns.
    /// </remarks>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp until which this row is leased by an active dispatch attempt.
    /// <see langword="null"/> means no active lease.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    public int Retries { get; set; }

    public string? ExceptionInfo { get; set; }
}
