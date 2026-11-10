// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Provider-neutral, payload-free administration of published rows: audited re-send and delete.</summary>
/// <remarks>
/// Every write records a receipt in the operator ledger, refused ones included, in the transaction that changes the
/// row, so <see cref="IInboxOperationsApi.QueryReceiptsAsync"/> filtered by
/// <see cref="MessagingOperationTargetKind.Published"/> reads them. The ledger lives in the primary store; an
/// additional outbox's database holds none, so its writes throw <see cref="NotSupportedException"/>.
/// </remarks>
[PublicAPI]
public interface IPublishedOperationsApi
{
    /// <summary>
    /// Counts the rows in an Operation's frozen scope, stopping at <paramref name="cap"/>, so the confirm step can show
    /// the reach.
    /// </summary>
    /// <remarks>See <see cref="ReadOperationScopeAsync"/> for which rows are in scope.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The scope's query does not read published rows.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="cap"/> is not positive, or the scope's kind is neither <see cref="MessageOperationKind.Resend"/>
    /// nor <see cref="MessageOperationKind.Delete"/>.
    /// </exception>
    ValueTask<MessageCount> CountOperationScopeAsync(
        MessageOperationScope scope,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns at most <paramref name="chunkSize"/> rows of an Operation's frozen scope, in the store's identifier order
    /// after <paramref name="afterId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row is in scope when the scope's query matches it, its change marker is at or before
    /// <see cref="MessageOperationScope.HighWaterMark"/>, and it is terminal: <see cref="StatusName.Succeeded"/> or
    /// <see cref="StatusName.Failed"/> with no retry pending. Re-send also needs it <see cref="StatusName.Failed"/>.
    /// </para>
    /// <para>
    /// Apply each target through <see cref="ResendAsync"/> or <see cref="PurgeAsync"/> with the target's
    /// <see cref="MessageOperationTarget.ChangedAt"/> as <see cref="PublishedOperationRequest.ExpectedChangedAt"/>.
    /// Those calls apply only while the row still carries that marker, so any outcome other than
    /// <see cref="InboxOperationOutcome.Applied"/> is a refusal, not a failure: another node or an earlier run already
    /// changed the row.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The scope's query does not read published rows.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chunkSize"/> is not between 1 and 500, or the scope's kind is neither
    /// <see cref="MessageOperationKind.Resend"/> nor <see cref="MessageOperationKind.Delete"/>.
    /// </exception>
    ValueTask<MessageOperationChunk> ReadOperationScopeAsync(
        MessageOperationScope scope,
        Guid? afterId,
        int chunkSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a published row that failed for good again: its retry counters reset, its lease and retention expiry
    /// clear, and its next retry becomes the store's clock, so the retry processor sends it on its next pickup.
    /// </summary>
    /// <remarks>
    /// Applies only to a <see cref="StatusName.Failed"/> row with no retry pending. A row still waiting for a send or a
    /// retry is refused as <see cref="InboxOperationOutcome.Active"/>, a succeeded row or a stale
    /// <see cref="PublishedOperationRequest.ExpectedChangedAt"/> as <see cref="InboxOperationOutcome.StateConflict"/>,
    /// and a missing row as <see cref="InboxOperationOutcome.NotFound"/>.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The request does not name an authenticated operator.</exception>
    /// <exception cref="InvalidOperationException">The request has an empty identity or an over-long reason.</exception>
    /// <exception cref="NotSupportedException">The store holds no operator ledger.</exception>
    ValueTask<PublishedOperationResult> ResendAsync(
        PublishedOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes a terminal published row while keeping its receipt.</summary>
    /// <remarks>
    /// Applies only to a terminal row. A row still waiting for a send or a retry is refused as
    /// <see cref="InboxOperationOutcome.Active"/>, a stale <see cref="PublishedOperationRequest.ExpectedChangedAt"/> as
    /// <see cref="InboxOperationOutcome.StateConflict"/>, and a missing row as <see cref="InboxOperationOutcome.NotFound"/>.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The request does not name an authenticated operator.</exception>
    /// <exception cref="InvalidOperationException">The request has an empty identity or an over-long reason.</exception>
    /// <exception cref="NotSupportedException">The store holds no operator ledger.</exception>
    ValueTask<PublishedOperationResult> PurgeAsync(
        PublishedOperationRequest request,
        CancellationToken cancellationToken = default
    );
}
