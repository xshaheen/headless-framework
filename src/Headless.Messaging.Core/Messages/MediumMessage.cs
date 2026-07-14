// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Messages;

[PublicAPI]
public class MediumMessage
{
    public required Guid StorageId { get; set; }

    public required Message Origin { get; set; }

    public required string Content { get; set; }

    public required IntentType IntentType { get; set; }

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

    /// <summary>
    /// Gets or sets the node-incarnation owner that currently holds this row's active lease.
    /// <see langword="null"/> means no Coordination-backed owner was stamped.
    /// </summary>
    public string? Owner { get; set; }

    public int Retries { get; set; }

    /// <summary>
    /// Gets or sets the number of delivery attempts durably reserved in the current inline burst.
    /// The reservation is written before invoking user code or a transport so crash recovery cannot
    /// grant a fresh inline budget. It resets to zero when <see cref="Retries"/> advances.
    /// </summary>
    public int InlineAttempts { get; set; }

    public string? ExceptionInfo { get; set; }
}
