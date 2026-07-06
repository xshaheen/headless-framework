// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

/// <summary>
/// A dashboard-facing projection of a single message row from the published or received table.
/// </summary>
[PublicAPI]
public class MessageView
{
    /// <summary>Gets or sets the internal storage row identifier.</summary>
    public required Guid StorageId { get; set; }

    /// <summary>Gets or sets the application-assigned message id (from the <c>MessageId</c> header).</summary>
    public required string MessageId { get; set; }

    /// <summary>Gets or sets the messaging framework version that stored this row.</summary>
    public required string Version { get; set; }

    /// <summary>Gets or sets the consumer group that the row is targeted at, or <see langword="null"/> for bus-broadcast rows without a pinned group.</summary>
    public string? Group { get; set; }

    /// <summary>Gets or sets the message name (topic or queue name) resolved at publish time.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the delivery intent that produced this row.</summary>
    public IntentType IntentType { get; set; }

    /// <summary>Gets or sets the serialized message body, or <see langword="null"/> when the content was not projected.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which the row was inserted into storage.</summary>
    public DateTime Added { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which this row will be purged by the expiry cleaner, or <see langword="null"/> for non-expiring rows.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Gets or sets the number of delivery attempts made for this message.</summary>
    public int Retries { get; set; }

    /// <summary>Gets or sets the current status of this message row.</summary>
    public required StatusName StatusName { get; set; }

    /// <summary>UTC timestamp at which the retry processor should re-dispatch this message; <see langword="null"/> when the row is terminal or has no pending retry.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>UTC timestamp until which the row is leased by an active dispatch attempt; <see langword="null"/> when no lease is active.</summary>
    public DateTime? LockedUntil { get; set; }
}
