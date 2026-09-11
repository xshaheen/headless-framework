// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;

namespace Headless.Messaging.Messages;

/// <summary>
/// Storage-level envelope for a persisted outbox/inbox message row: pairs the original
/// <see cref="Message"/> with its serialized content and the durable dispatch/retry bookkeeping
/// columns (retries, lease, scheduling) that the storage providers and retry processors maintain.
/// </summary>
/// <remarks>
/// Not sealed: storage providers may extend it with backend-specific row state
/// (e.g., the in-memory provider's message row adds name/group/status columns).
/// </remarks>
[PublicAPI]
public class MediumMessage
{
    public required Guid StorageId { get; set; }

    public required Message Origin { get; set; }

    public required string Content { get; set; }

    /// <summary>The optional affinity key from the authoritative persisted envelope.</summary>
    public string? RoutingAffinityKey =>
        Origin.Headers.TryGetValue(Headers.RoutingAffinityKey, out var key) ? key : null;

    public required MessageLane Lane { get; set; }

    public DateTimeOffset Added { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp at which the persisted retry processor should re-dispatch
    /// this message. <see langword="null"/> means no scheduled retry.
    /// </summary>
    /// <remarks>
    /// A <see cref="DateTimeOffset"/>, so the instant is unforgeable: unlike <c>DateTime</c>, it cannot arrive
    /// with an ambiguous kind that a provider or serializer silently reinterprets.
    /// </remarks>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp until which this row is leased by an active dispatch attempt.
    /// <see langword="null"/> means no active lease.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

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

    /// <summary>The durable logical inbox key when this is a received inbox row.</summary>
    public InboxKey? InboxKey { get; set; }

    /// <summary>The immutable generation identity allocated at admission.</summary>
    public InboxGeneration? InboxGeneration { get; set; }

    /// <summary>The exact active attempt fence, populated only after a successful reservation.</summary>
    public InboxAttemptFence? InboxAttemptFence { get; set; }

    /// <summary>Whether persisted recovery currently has no matching stable consumer registration.</summary>
    public bool IsInboxOrphaned { get; set; }

    public string? ExceptionInfo { get; set; }
}
