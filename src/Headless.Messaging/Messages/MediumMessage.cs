// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Messaging.Persistence;

namespace Headless.Messaging;

/// <summary>
/// Storage-level envelope for a persisted outbox/inbox message row: pairs the original
/// <see cref="Message"/> with its serialized content and the durable dispatch/retry bookkeeping
/// columns (retries, lease, scheduling) that the storage providers and retry processors maintain.
/// </summary>
/// <remarks>
/// Not sealed: storage providers may extend it with backend-specific row state
/// (e.g., the in-memory provider's message row adds name/consumer identity/status columns).
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
    /// Gets or sets the row's lease as storage last read or wrote it: the generation an attempt holds, its state, and its
    /// expiry by the database clock. A row that holds a lease is <see cref="RowLeaseState.Active" />; the
    /// <see langword="default" /> is a row never leased.
    /// </summary>
    public RowLeaseSnapshot Lease { get; set; }

    public int Retries { get; set; }

    /// <summary>
    /// Gets or sets the row's stored status as the monitoring API read it. <see langword="null"/> when the row was not
    /// read through the monitoring API, which is the only reader that loads it.
    /// </summary>
    public StatusName? StatusName { get; set; }

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

    /// <summary>
    /// The circuit epoch assigned to the dispatch admission that owns a HalfOpen probe release.
    /// This is in-memory handoff state only; storage providers must not persist it.
    /// </summary>
    public long ProbeEpoch { get; set; }

    /// <summary>
    /// The additional outbox storage that holds this published row, or <see langword="null" /> for a row of the
    /// primary storage. In-memory handoff state only: core stamps it where it writes or picks up a row from an
    /// additional outbox, so every later state change reaches that row's own database.
    /// </summary>
    internal IDataStorage? OutboxStorage { get; set; }

    /// <summary>
    /// Whether <see cref="Origin"/> was changed after it was last written to storage. A state transition that
    /// follows must rewrite the stored envelope; one that does not can leave it alone. In-memory state only.
    /// </summary>
    internal bool HasUnpersistedOriginChanges { get; set; }

    /// <summary>
    /// The transport message serialized at publish for the envelope it names, so the send that follows reuses the
    /// payload bytes the stored row was built from instead of serializing the same object again. In-memory handoff
    /// state only; a row read back from storage has none.
    /// </summary>
    internal (Message Origin, TransportMessage Transport)? PreparedTransport { get; set; }

    public string? ExceptionInfo { get; set; }

    /// <summary>
    /// The versioned fingerprint of the failure in <see cref="ExceptionInfo"/>, which groups failures by cause; see
    /// <see cref="MessagingFailureFingerprints"/>. Set by the failure paths before the state write, which stores it in
    /// the same statement; <see langword="null"/> on a row that has not failed.
    /// </summary>
    public string? FailureFingerprint { get; set; }
}
