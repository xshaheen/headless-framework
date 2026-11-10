// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

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

    /// <summary>Gets or sets the identity of the consumer a received row belongs to, or <see langword="null"/> for published rows.</summary>
    public string? ConsumerIdentity { get; set; }

    /// <summary>Gets or sets the message name (topic or queue name) resolved at publish time.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the delivery lane that produced this row.</summary>
    public MessageLane Lane { get; set; }

    /// <summary>Gets the caller-requested delivery mode, or <see langword="null"/> for legacy/unreadable metadata.</summary>
    public DeliveryMode? RequestedDeliveryMode { get; set; }

    /// <summary>
    /// Gets the resolved delivery mode. Legacy readable stored envelopes resolve to <see cref="DeliveryMode.Durable"/>;
    /// unreadable envelopes expose <see langword="null"/> without failing the containing page.
    /// </summary>
    public DeliveryMode? ResolvedDeliveryMode { get; set; }

    /// <summary>
    /// Gets or sets whether the row was written inside the caller's unit-of-work transaction, or
    /// <see langword="null"/> when the row records no answer (legacy or unreadable metadata). A null here means
    /// unknown, never "not coordinated".
    /// </summary>
    public bool? IsCoordinated { get; set; }

    /// <summary>Gets or sets the serialized message body, or <see langword="null"/> when the content was not projected.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which the row was inserted into storage.</summary>
    public DateTimeOffset Added { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which this row will be purged by the expiry cleaner, or <see langword="null"/> for non-expiring rows.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Gets or sets the number of delivery attempts made for this message.</summary>
    public int Retries { get; set; }

    /// <summary>Gets or sets the current status of this message row.</summary>
    public required StatusName StatusName { get; set; }

    /// <summary>UTC timestamp at which the retry processor should re-dispatch this message; <see langword="null"/> when the row is terminal or has no pending retry.</summary>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>UTC timestamp until which the row is leased by an active dispatch attempt; <see langword="null"/> when no lease is active.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// Gets or sets the row's change marker: when a write last changed its visible state. Set by
    /// <see cref="IMonitoringApi.ListMessagesAsync"/>.
    /// </summary>
    public DateTimeOffset? ChangedAt { get; set; }

    /// <summary>
    /// Gets or sets the fingerprint of the row's last failure, or <see langword="null"/>. Set by
    /// <see cref="IMonitoringApi.ListMessagesAsync"/>.
    /// </summary>
    public string? FailureFingerprint { get; set; }

    /// <summary>
    /// Gets or sets the tenant of a received inbox row, or <see langword="null"/>. Set by
    /// <see cref="IMonitoringApi.ListMessagesAsync"/>.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets whether a received inbox row is held. Set by <see cref="IMonitoringApi.ListMessagesAsync"/>.
    /// </summary>
    public bool IsHeld { get; set; }

    /// <summary>
    /// Gets or sets a received inbox row's incarnation, the identity its audited operations fence on, or
    /// <see langword="null"/>. Set by <see cref="IMonitoringApi.ListMessagesAsync"/>.
    /// </summary>
    public Guid? IncarnationId { get; set; }
}
