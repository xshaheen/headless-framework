// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Messaging.Persistence;

[PublicAPI]
public interface IDataStorage
{
    /// <summary>Returns the monitoring API for this storage provider, used by the dashboard and operator tooling.</summary>
    IMonitoringApi GetMonitoringApi();

    /// <summary>Returns the audited, provider-neutral inbox administration API.</summary>
    IInboxOperationsApi GetInboxOperationsApi();

    /// <summary>Returns the audited, provider-neutral scheduled delivery administration API.</summary>
    IScheduledDeliveryOperationsApi GetScheduledDeliveryOperationsApi() =>
        throw new NotSupportedException(
            $"Messaging storage provider '{GetType().FullName}' does not support scheduled-delivery operations."
        );

    /// <summary>Reads one provider-clock snapshot with the current history retention settings.</summary>
    /// <remarks>Use database time for SQL providers and the injected clock for InMemory. Reuse this snapshot throughout a collector invocation.</remarks>
    ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes at most <paramref name="batchSize"/> expired audits without creating new history.</summary>
    /// <remarks>Creation timestamps equal to a cutoff are expired. Deletion removes evidence but does not release generation holds.</remarks>
    ValueTask<int> DeleteExpiredInboxAuditsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes at most <paramref name="batchSize"/> expired receipts with no remaining audit references. Replay survives until physical deletion.</summary>
    /// <remarks>Serialize deletion with operation replay and recheck age and references. After deletion, the same operation ID can be evaluated against current state as a new request.</remarks>
    ValueTask<int> DeleteExpiredInboxReceiptsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically converges a complete received envelope on its logical inbox key before transport settlement.
    /// Only <see cref="InboxAdmissionDisposition.Winner"/> may be dispatched by the caller.
    /// </summary>
    async ValueTask<InboxAdmissionResult> AdmitReceivedMessageAsync(
        string name,
        string consumerIdentity,
        string contractVersion,
        MediumMessage message,
        long generation = 0,
        TimeSpan? inboxRetention = null,
        CancellationToken cancellationToken = default
    )
    {
        var stored = await StoreReceivedMessageAsync(name, consumerIdentity, message, cancellationToken)
            .ConfigureAwait(false);
        return new InboxAdmissionResult(InboxAdmissionDisposition.Winner, stored);
    }

    /// <summary>
    /// Reports whether an inbox generation of this message was already admitted for the consumer, whatever its contract
    /// version, so a redelivery that fails before admission can be recognized as a duplicate of work that already ran
    /// rather than stored as poison.
    /// </summary>
    /// <param name="tenantId">The tenant the delivery names, or <see langword="null"/> for none.</param>
    /// <param name="messageId">The message id, matched exactly, trailing whitespace included.</param>
    /// <param name="messageName">
    /// The message name, matched exactly. Admission keys a generation on it, and a producer may reuse one explicit id
    /// across message names, so another name under the same id is a different message, not a redelivery.
    /// </param>
    /// <param name="consumerIdentity">The identity of the consumer the message was delivered to, matched exactly.</param>
    /// <param name="lane">The lane the message was delivered on.</param>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>
    /// <see langword="true"/> when an inbox row for this tenant, message id, message name, consumer identity, and lane
    /// exists. A
    /// provider without an inbox admits nothing and answers <see langword="false"/>.
    /// </returns>
    ValueTask<bool> IsInboxMessageAdmittedAsync(
        string? tenantId,
        string messageId,
        string messageName,
        string consumerIdentity,
        MessageLane lane,
        CancellationToken cancellationToken = default
    );

    /// <summary>Defers a missing registration by the configured orphan probe interval and releases ownership atomically under the complete attempt fence. Does not consume failure retries.</summary>
    ValueTask<bool> DeferReceivedInboxOrphanAsync(MediumMessage message, CancellationToken cancellationToken = default);

    /// <summary>Confirms routability under the complete live attempt fence and clears orphan state. Returns true even when already routable; false forbids dispatch.</summary>
    ValueTask<bool> ConfirmReceivedInboxRoutableAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>Claims due known orphans using a separate capped batch per lane, minting fresh attempts in the existing generation.</summary>
    /// <param name="lane">The lane whose orphans to claim.</param>
    /// <param name="consumerIdentities">
    /// The consumer identities the calling host consumes, or <see langword="null"/> to claim rows of every identity.
    /// Applied inside the claim, before the batch limit, so a row outside the set is never leased; an empty set
    /// claims nothing.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<IEnumerable<MediumMessage>> GetReceivedInboxOrphansOfNeedRetryAsync(
        MessageLane lane,
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hands the specified published message rows back to the <c>Delayed</c> state and clears their lease, but only
    /// while each row still carries the lease its message recorded.
    /// </summary>
    /// <remarks>
    /// A row matches when its <see cref="MediumMessage.Owner"/> and <see cref="MediumMessage.LockedUntil"/> equal the
    /// message's recorded values, both <see langword="null"/> included: an unleased copy resets only an unleased row,
    /// and a claimed copy resets only the exact lease generation it claimed. A row another node has leased since, an
    /// unknown row, and a terminal row are left unchanged.
    /// </remarks>
    /// <param name="messages">The messages to hand back, each carrying the lease identity it holds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask ChangePublishStateToDelayedAsync(
        IReadOnlyCollection<MediumMessage> messages,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates the status of a published message in storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ExceptionInfo asymmetry:</b> <see cref="MediumMessage.ExceptionInfo"/> is intentionally
    /// not persisted on the publish path; only the receive path (<see cref="ChangeReceiveStateAsync"/>)
    /// persists exception info because the <c>Published</c> table schema has no <c>ExceptionInfo</c>
    /// column. A 4th-provider implementation should mirror this asymmetry — never persist
    /// <c>ExceptionInfo</c> from this method.
    /// </para>
    /// </remarks>
    /// <param name="message">The message whose state is changing.</param>
    /// <param name="state">The new status to persist.</param>
    /// <param name="contentWrite">
    /// Whether the transition also rewrites the persisted envelope. Defaults to
    /// <see cref="MessageContentWrite.Preserve"/> because a status transition does not change the envelope;
    /// pass <see cref="MessageContentWrite.Refresh"/> only after mutating <see cref="MediumMessage.Origin"/>.
    /// </param>
    /// <param name="transaction">Optional ambient relational transaction.</param>
    /// <param name="retryDelay">
    /// When the retry processor should re-dispatch this message, as a delay the STORE adds to its own clock, the same
    /// clock retry pickup compares against. Pass <see langword="null"/> to clear the persisted due time. Only
    /// retry-transition paths pass a value.
    /// </param>
    /// <param name="lockedUntil">
    /// UTC timestamp until which the row is leased by an active dispatch attempt. Pass
    /// <see langword="null"/> on terminal and retry-schedule writes so the row becomes
    /// pickup-eligible again. Only retry-transition or in-flight paths supply a value; the
    /// pre-attempt lease itself is taken via the Lease* methods, which let the STORE derive the deadline.
    /// </param>
    /// <param name="originalRetries">
    /// Optimistic-concurrency token. When supplied, storage applies <c>AND Retries = @OriginalRetries</c>
    /// to the conditional update so a concurrent replica's advance cannot be silently overwritten.
    /// Pass <see langword="null"/> on non-retry transitions where counter-race protection is unneeded.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the row was updated; <see langword="false"/> when the row was already in a terminal state
    /// (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled retry),
    /// the row was not found, or the optimistic <paramref name="originalRetries"/> predicate did not match.
    /// </returns>
    ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        DbTransaction? transaction = null,
        RetryDelay? retryDelay = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Updates published retry state with optimistic checks for both durable retry counters.</summary>
    /// <param name="contentWrite">
    /// Whether the transition also rewrites the persisted envelope. See <see cref="MessageContentWrite"/>.
    /// </param>
    ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically reserves the next published delivery attempt under the active lease.</summary>
    /// <remarks>
    /// The reservation also slides the lease forward: the store re-stamps <c>LockedUntil</c> to its own clock plus
    /// <c>RetryPolicy.DispatchTimeout</c> in the same statement, so a row reserved late in a claimed batch still holds a
    /// full lease when its attempt starts. It applies only while the caller's <c>Owner</c> and <c>LockedUntil</c> still
    /// match the row and that lease is live by the store's clock. On success the caller's <c>LockedUntil</c>,
    /// <c>Owner</c>, and inbox attempt fence carry the new lease identity, which every later fenced write uses.
    /// </remarks>
    ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Slides the active lease on a published message forward while its attempt runs, so a send that outlasts
    /// <c>RetryPolicy.DispatchTimeout</c> keeps its row and is not dispatched a second time by another node.
    /// </summary>
    /// <remarks>
    /// Applies only while the caller's <c>Owner</c> and <c>LockedUntil</c> still match a non-terminal row and that
    /// lease is live by the store's clock; the store stamps the new deadline from that same clock. On success the
    /// caller's <c>LockedUntil</c> carries the new lease identity. The default throws
    /// <see cref="NotSupportedException"/>; the core then keeps the lease it reserved for the attempt.
    /// </remarks>
    /// <param name="message">The message whose lease the caller holds.</param>
    /// <param name="leaseDuration">How long the renewed lease lasts from the store's clock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the lease was renewed; <see langword="false"/> when the caller no longer holds it
    /// (it expired, another node took it, or the row reached a terminal state). The caller must stop the attempt and
    /// write no state on <see langword="false"/>.
    /// </returns>
    /// <exception cref="NotSupportedException">The storage provider does not support lease renewal.</exception>
    ValueTask<bool> RenewPublishLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    ) =>
        throw new NotSupportedException(
            $"Messaging storage provider '{GetType().FullName}' does not support lease renewal."
        );

    /// <summary>
    /// Writes a pre-attempt lease on a published message, setting <c>LockedUntil</c> on the row so
    /// the persisted retry processor excludes it while a dispatch attempt is active.
    /// </summary>
    /// <param name="message">The message to lease. On success the caller's <c>LockedUntil</c> is also updated.</param>
    /// <param name="leaseDuration">
    /// How long the lease should last. The STORE derives the absolute deadline from its own clock, so a lease
    /// written by one node and evaluated by another cannot be skewed by either node's wall clock. A negative
    /// duration yields a deadline already in the store's past (an expired lease).
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the lease was written; <see langword="false"/> when the row was already in a terminal
    /// state (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled
    /// retry) or the row was not found. Callers must stop the attempt path on <see langword="false"/>.
    /// </returns>
    ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically acquires the dispatch lease AND durably reserves the next inline delivery attempt
    /// in a single statement — the fresh-dispatch fast path that replaces a
    /// <see cref="LeasePublishAsync"/> + <see cref="ReservePublishAttemptAsync"/> pair with one
    /// round trip. Succeeds only when the row is non-terminal, unleased or lease-expired, and both
    /// durable counters still match the caller's view (<c>Retries</c> and
    /// <paramref name="originalInlineAttempts"/>).
    /// </summary>
    /// <param name="message">
    /// The message to lease. The caller pre-increments <c>InlineAttempts</c> (the reservation value
    /// written on success) and rolls it back when this method returns <see langword="false"/>. On
    /// success the caller's <c>LockedUntil</c> and <c>Owner</c> are also updated.
    /// </param>
    /// <param name="leaseDuration">
    /// How long the lease should last. The STORE derives the absolute deadline from its own clock, so a lease
    /// written by one node and evaluated by another cannot be skewed by either node's wall clock. A negative
    /// duration yields a deadline already in the store's past (an expired lease).
    /// </param>
    /// <param name="originalInlineAttempts">Optimistic-concurrency token for <c>InlineAttempts</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the lease and reservation were written; <see langword="false"/> when the row is
    /// terminal, actively leased by another owner, counter state moved, or the row was not found.
    /// Callers must stop the attempt path on <see langword="false"/>.
    /// </returns>
    ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates the status of a received message in storage.
    /// </summary>
    /// <param name="message">The message whose state is changing.</param>
    /// <param name="state">The new status to persist.</param>
    /// <param name="contentWrite">
    /// Whether the transition also rewrites the persisted envelope. Defaults to
    /// <see cref="MessageContentWrite.Preserve"/> because a status transition does not change the envelope;
    /// pass <see cref="MessageContentWrite.Refresh"/> only after mutating <see cref="MediumMessage.Origin"/>.
    /// </param>
    /// <param name="retryDelay">
    /// When the retry processor should re-dispatch this message, as a delay the STORE adds to its own clock, the same
    /// clock retry pickup compares against. Pass <see langword="null"/> to clear the persisted due time. Only
    /// retry-transition paths pass a value.
    /// </param>
    /// <param name="lockedUntil">
    /// UTC timestamp until which the row is leased by an active dispatch attempt. Pass
    /// <see langword="null"/> on terminal and retry-schedule writes so the row becomes
    /// pickup-eligible again. Only retry-transition or in-flight paths supply a value; the
    /// pre-attempt lease itself is taken via the Lease* methods, which let the STORE derive the deadline.
    /// </param>
    /// <param name="originalRetries">
    /// Optimistic-concurrency token. When supplied, storage applies <c>AND Retries = @OriginalRetries</c>
    /// to the conditional update so a concurrent replica's advance cannot be silently overwritten.
    /// Pass <see langword="null"/> on non-retry transitions where counter-race protection is unneeded.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the row was updated; <see langword="false"/> when the row was already in a terminal state
    /// (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled retry),
    /// the row was not found, or the optimistic <paramref name="originalRetries"/> predicate did not match.
    /// </returns>
    ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        RetryDelay? retryDelay = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Updates received retry state with optimistic checks for both durable retry counters.</summary>
    /// <param name="contentWrite">
    /// Whether the transition also rewrites the persisted envelope. See <see cref="MessageContentWrite"/>.
    /// </param>
    ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically reserves the next received delivery attempt under the active lease.</summary>
    /// <remarks>
    /// The reservation also slides the lease forward: the store re-stamps <c>LockedUntil</c> to its own clock plus
    /// <c>RetryPolicy.DispatchTimeout</c> in the same statement, so a row reserved late in a claimed batch still holds a
    /// full lease when its attempt starts. It applies only while the caller's <c>Owner</c> and <c>LockedUntil</c> still
    /// match the row and that lease is live by the store's clock. On success the caller's <c>LockedUntil</c>,
    /// <c>Owner</c>, and inbox attempt fence carry the new lease identity, which every later fenced write uses.
    /// </remarks>
    ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Slides the active lease on a received message forward while its handler runs, so a handler that outlasts
    /// <c>RetryPolicy.DispatchTimeout</c> keeps its row and does not run a second time on another node.
    /// </summary>
    /// <remarks>
    /// Same contract as <see cref="RenewPublishLeaseAsync"/>; an inbox row also requires the caller's complete inbox
    /// attempt fence, and on success that fence carries the new lease identity too.
    /// </remarks>
    /// <param name="message">The message whose lease the caller holds.</param>
    /// <param name="leaseDuration">How long the renewed lease lasts from the store's clock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the lease was renewed; <see langword="false"/> when the caller no longer holds it.
    /// </returns>
    /// <exception cref="NotSupportedException">The storage provider does not support lease renewal.</exception>
    ValueTask<bool> RenewReceiveLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    ) =>
        throw new NotSupportedException(
            $"Messaging storage provider '{GetType().FullName}' does not support lease renewal."
        );

    /// <summary>
    /// Writes a pre-attempt lease on a received message, setting <c>LockedUntil</c> on the row so
    /// the persisted retry processor excludes it while a consume attempt is active.
    /// </summary>
    /// <param name="message">The message to lease. On success the caller's <c>LockedUntil</c> is also updated.</param>
    /// <param name="leaseDuration">
    /// How long the lease should last. The STORE derives the absolute deadline from its own clock, so a lease
    /// written by one node and evaluated by another cannot be skewed by either node's wall clock. A negative
    /// duration yields a deadline already in the store's past (an expired lease).
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the lease was written; <see langword="false"/> when the row was already in a terminal
    /// state (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled
    /// retry) or the row was not found. Callers must stop the attempt path on <see langword="false"/>.
    /// </returns>
    ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically acquires the consume lease AND durably reserves the next inline delivery attempt
    /// in a single statement — the fresh-dispatch fast path that replaces a
    /// <see cref="LeaseReceiveAsync"/> + <see cref="ReserveReceiveAttemptAsync"/> pair with one
    /// round trip. Same contract as <see cref="LeasePublishAndReserveAttemptAsync"/>.
    /// </summary>
    ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists an outbound message envelope to the published table and returns the stored row with its assigned <c>StorageId</c>.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) resolved at publish time.</param>
    /// <param name="message">The message envelope to persist.</param>
    /// <param name="transaction">
    /// Optional ambient relational transaction. When supplied, the insert participates in the caller's transaction.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored envelope with <c>StorageId</c> populated by the storage provider.</returns>
    ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists a delayed outbound message with its not-before state in the same write as the envelope.
    /// </summary>
    /// <remarks>
    /// Implementations must commit the row's schedule atomically. Dispatch after this write is acceleration;
    /// durable delayed-message pickup remains the recovery authority when the process stops before enqueueing it.
    /// </remarks>
    ValueTask<MediumMessage> StoreScheduledMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset publishAt,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists an outbound <see cref="Message"/> to the published table, wrapping it in a
    /// <c>MediumMessage</c> envelope before delegating to the primary overload.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) resolved at publish time.</param>
    /// <param name="content">The raw message to wrap and persist.</param>
    /// <param name="transaction">
    /// Optional ambient transaction. When supplied, the insert participates in the caller's transaction.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored envelope with <c>StorageId</c> populated by the storage provider.</returns>
    ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return StoreMessageAsync(
            name,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = content,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            transaction,
            cancellationToken
        );
    }

    /// <summary>
    /// Stores a failed received message envelope in the received table so operators can inspect
    /// and replay messages that caused unhandled exceptions.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) as received from the broker.</param>
    /// <param name="consumerIdentity">The identity of the consumer the message was delivered to; received rows are unique per message ID and consumer identity.</param>
    /// <param name="message">The failed received message envelope, including serialized content and delivery intent.</param>
    /// <param name="exceptionInfo">Optional serialized exception details to persist alongside the failed row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the row was stored; <see langword="false"/> when storage was skipped or failed.</returns>
    ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores a failed received message using raw serialized content when no <c>MediumMessage</c> envelope is available.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) as received from the broker.</param>
    /// <param name="consumerIdentity">The identity of the consumer the message was delivered to; received rows are unique per message ID and consumer identity.</param>
    /// <param name="content">The raw serialized message body.</param>
    /// <param name="exceptionInfo">Optional serialized exception details to persist alongside the failed row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the row was stored; <see langword="false"/> when storage was skipped or failed.</returns>
    ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists an inbound message envelope to the received table and returns the stored row with its assigned <c>StorageId</c>.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) as received from the broker.</param>
    /// <param name="consumerIdentity">The identity of the consumer the message was delivered to; received rows are unique per message ID and consumer identity.</param>
    /// <param name="message">The received message envelope to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored envelope with <c>StorageId</c> populated by the storage provider.</returns>
    ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists an inbound <see cref="Message"/> to the received table, wrapping it in a
    /// <c>MediumMessage</c> envelope before delegating to the primary overload.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) as received from the broker.</param>
    /// <param name="consumerIdentity">The identity of the consumer the message was delivered to; received rows are unique per message ID and consumer identity.</param>
    /// <param name="content">The raw message to wrap and persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored envelope with <c>StorageId</c> populated by the storage provider.</returns>
    ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        Message content,
        CancellationToken cancellationToken = default
    )
    {
        return StoreReceivedMessageAsync(
            name,
            consumerIdentity,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = content,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Deletes expired message rows from the specified table in bounded batches.
    /// </summary>
    /// <param name="table">The physical table name to clean (use <c>IStorageTableNames.GetPublishedTableName()</c> or <c>GetReceivedTableName()</c>).</param>
    /// <param name="timeout">Rows whose <c>ExpiresAt</c> is earlier than this UTC timestamp are eligible for deletion.</param>
    /// <param name="batchCount">Maximum number of rows to delete in a single statement (default 1000).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTimeOffset timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns published messages due for retry, filtered by <c>NextRetryAt &lt;= now()</c> on the store's clock,
    /// the clock that turned each retry's <see cref="RetryDelay"/> into its due time.
    /// No lookback window is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Atomic claim-and-return:</b> returned rows are already leased — the same statement that
    /// selects them advances <c>LockedUntil</c> to <c>now + RetryPolicyOptions.DispatchTimeout</c>. Relational
    /// providers use their database clock for the due-time comparison, the lease-expiry comparison, and stamping.
    /// In-memory providers use their injected <see cref="TimeProvider"/> for all three.
    /// Callers do NOT need to invoke <see cref="LeasePublishAsync"/> immediately after pickup; the
    /// pickup itself is the claim. This prevents two replicas from picking up the same row between
    /// a SELECT commit and a follow-up lease write (the prior two-step design double-dispatched).
    /// </para>
    /// <para>
    /// Replicas with a stale view will skip these rows because the lease is now in the future.
    /// The dispatch path will still call <see cref="LeasePublishAsync"/> to refresh the lease per
    /// attempt; that lease overwrites the pickup-grant value with a fresh
    /// <c>now + DispatchTimeout</c>.
    /// </para>
    /// </remarks>
    ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        MessageLane lane,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Accelerates retry visibility for published rows leased by dead node-incarnation owners.
    /// </summary>
    /// <param name="deadOwners">
    /// Serialized dead node-incarnation owners whose still-leased rows should be reclaimed. An empty
    /// collection is a no-op and returns <c>0</c>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows reclaimed.</returns>
    ValueTask<int> ReclaimDeadPublishedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Streams delayed published messages to <paramref name="scheduleTask"/> for re-scheduling onto
    /// the publish path.
    /// </summary>
    /// <param name="scheduleTask">
    /// Callback invoked with the active transaction handle and the matching delayed messages. The
    /// transaction handle is non-null when a transactional provider is in use (PostgreSQL, SQL Server)
    /// and <see langword="null"/> for non-transactional providers (InMemory). Callers MUST null-check
    /// before casting.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask ScheduleMessagesOfDelayedAsync(
        Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns received messages due for retry, filtered by <c>NextRetryAt &lt;= now()</c> on the store's clock,
    /// the clock that turned each retry's <see cref="RetryDelay"/> into its due time.
    /// No lookback window is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Atomic claim-and-return:</b> returned rows are already leased — the same statement that
    /// selects them advances <c>LockedUntil</c> to <c>now + RetryPolicyOptions.DispatchTimeout</c>. Relational
    /// providers use their database clock for the due-time comparison, the lease-expiry comparison, and stamping.
    /// In-memory providers use their injected <see cref="TimeProvider"/> for all three.
    /// Callers do NOT need to invoke <see cref="LeaseReceiveAsync"/> immediately after pickup; the
    /// pickup itself is the claim. This prevents two replicas from picking up the same row between
    /// a SELECT commit and a follow-up lease write (the prior two-step design double-dispatched).
    /// </para>
    /// <para>
    /// Replicas with a stale view will skip these rows because the lease is now in the future.
    /// The consume path will still call <see cref="LeaseReceiveAsync"/> to refresh the lease per
    /// attempt; that lease overwrites the pickup-grant value with a fresh
    /// <c>now + DispatchTimeout</c>.
    /// </para>
    /// <para>
    /// <b>Consumer filter:</b> a host started with <c>ConsumeOnly</c> has executors for only some consumer identities.
    /// It passes those identities so it never leases another host's row: it could neither run nor classify one, and
    /// would otherwise fail it as having no subscriber or defer it as an inbox orphan.
    /// </para>
    /// </remarks>
    /// <param name="lane">The lane whose due rows to claim.</param>
    /// <param name="consumerIdentities">
    /// The consumer identities the calling host consumes, or <see langword="null"/> to claim rows of every identity.
    /// Applied inside the claim, before the batch limit, so a row outside the set is never leased; an empty set
    /// claims nothing.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        MessageLane lane,
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Accelerates retry visibility for received rows leased by dead node-incarnation owners.
    /// </summary>
    /// <param name="deadOwners">
    /// Serialized dead node-incarnation owners whose still-leased rows should be reclaimed. An empty
    /// collection is a no-op and returns <c>0</c>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows reclaimed.</returns>
    ValueTask<int> ReclaimDeadReceivedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a single received message row by its storage id.
    /// </summary>
    /// <param name="id">The internal storage row identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted (0 or 1).</returns>
    ValueTask<int> DeleteReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple received message rows by their storage ids in a single operation.
    /// </summary>
    /// <param name="ids">The internal storage row identifiers to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> DeleteReceivedMessagesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single published message row by its storage id.
    /// </summary>
    /// <param name="id">The internal storage row identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted (0 or 1).</returns>
    ValueTask<int> DeletePublishedMessageAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple published message rows by their storage ids in a single operation.
    /// </summary>
    /// <param name="ids">The internal storage row identifiers to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> DeletePublishedMessagesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues published message rows that failed for good, so the retry processor sends each one again with a fresh
    /// retry budget.
    /// </summary>
    /// <remarks>
    /// Only a row that is <see cref="StatusName.Failed"/> with no <c>NextRetryAt</c> is requeued: its retry counters
    /// reset to zero, its lease and retention expiry clear, and its <c>NextRetryAt</c> becomes the store's clock, so the
    /// next retry pickup claims it. A succeeded row, a row still waiting for a retry, and a row that does not exist are
    /// left unchanged. Each row is decided on its own, so a concurrent writer of one row does not affect the others.
    /// </remarks>
    /// <param name="ids">The internal storage row identifiers to requeue.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identifiers of the rows that were requeued.</returns>
    ValueTask<IReadOnlyList<Guid>> RequeuePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Optional storage capability that atomically claims delayed published messages for local scheduling.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must select no more than the configured scheduler batch size, transition each
/// winner to <see cref="StatusName.Queued"/>, and stamp an ownership lease from the same authoritative
/// store clock used to test lease expiry. For a future schedule, the lease must extend from the later
/// of that clock or the message expiration time so it remains valid until the first dispatch attempt.
/// The returned messages must reflect the committed durable <c>LockedUntil</c> and <c>Owner</c> values.
/// </para>
/// <para>
/// Providers that do not implement this capability continue through
/// <see cref="IDataStorage.ScheduleMessagesOfDelayedAsync"/>. Callers must not enqueue returned messages
/// until this method completes because completion is the commit boundary.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDelayedMessageClaimStorage
{
    /// <summary>Atomically claims and transitions a bounded batch of delayed messages.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Committed claim winners in deterministic schedule order.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the durable claim commits.
    /// </exception>
    ValueTask<IReadOnlyList<MediumMessage>> ClaimDelayedMessagesAsync(CancellationToken cancellationToken = default);
}
