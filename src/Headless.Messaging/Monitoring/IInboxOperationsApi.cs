// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Messaging;

/// <summary>Provider-neutral, payload-free inbox administration surface.</summary>
[PublicAPI]
public interface IInboxOperationsApi
{
    /// <summary>Queries retained inbox generations without loading message payloads or arbitrary headers.</summary>
    /// <remarks>A read: <paramref name="authorization"/> needs an authenticated principal, not a named operator.</remarks>
    /// <exception cref="UnauthorizedAccessException">The principal is not authenticated.</exception>
    ValueTask<IndexPage<InboxGenerationView>> QueryAsync(
        InboxGenerationQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Counts the retained inbox generations <paramref name="query"/> matches, stopping at <paramref name="cap"/>; its
    /// paging members are ignored.
    /// </summary>
    /// <remarks>A read: <paramref name="authorization"/> needs an authenticated principal, not a named operator.</remarks>
    /// <exception cref="UnauthorizedAccessException">The principal is not authenticated.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not positive.</exception>
    ValueTask<MessageCount> CountAsync(
        InboxGenerationQuery query,
        OperatorAuthorizationContext authorization,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Counts the generations in an Operation's frozen scope, stopping at <paramref name="cap"/>, so the confirm step
    /// can show the reach.
    /// </summary>
    /// <remarks>See <see cref="ReadOperationScopeAsync"/> for which generations are in scope.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The scope's query does not read received rows.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not positive.</exception>
    ValueTask<MessageCount> CountOperationScopeAsync(
        MessageOperationScope scope,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns at most <paramref name="chunkSize"/> generations of an Operation's frozen scope, in the store's
    /// identifier order after <paramref name="afterId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generation is in scope when it is an inbox generation the scope's query matches, its change marker is at or
    /// before <see cref="MessageOperationScope.HighWaterMark"/>, and it is terminal (<see cref="StatusName.Succeeded"/>
    /// or <see cref="StatusName.Failed"/> with no retry pending). Re-execute also needs it to be the current
    /// generation; delete needs it unheld.
    /// </para>
    /// <para>
    /// Apply each target through <see cref="ForceReprocessAsync"/> or <see cref="PurgeAsync"/> with the target's
    /// incarnation and status. Those calls check eligibility again under the row's lock, so any outcome other than
    /// <see cref="InboxOperationOutcome.Applied"/> is a refusal, not a failure: another node or an earlier run already
    /// changed the row.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The scope's query does not read received rows.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not between 1 and 500.</exception>
    ValueTask<MessageOperationChunk> ReadOperationScopeAsync(
        MessageOperationScope scope,
        Guid? afterId,
        int chunkSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the operator ledger: the receipts of every audited intervention, refused ones included, newest first.
    /// </summary>
    /// <remarks>A read: <paramref name="authorization"/> needs an authenticated principal, not a named operator.</remarks>
    /// <exception cref="UnauthorizedAccessException">The principal is not authenticated.</exception>
    ValueTask<MessagingOperationReceiptPage> QueryReceiptsAsync(
        MessagingOperationReceiptQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    );

    /// <summary>Adds a retention hold to an expected terminal generation.</summary>
    ValueTask<InboxOperationResult> HoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases the retention hold from an expected terminal generation.</summary>
    ValueTask<InboxOperationResult> ReleaseHoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically creates one independently deduplicated child generation.</summary>
    ValueTask<InboxOperationResult> ForceReprocessAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes an expected terminal, unheld generation while preserving its audit and receipt.</summary>
    ValueTask<InboxOperationResult> PurgeAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );
}
