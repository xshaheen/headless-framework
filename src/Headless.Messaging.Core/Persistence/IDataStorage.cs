// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Persistence;

[PublicAPI]
public interface IDataStorage
{
    /// <summary>Returns the monitoring API for this storage provider, used by the dashboard and operator tooling.</summary>
    IMonitoringApi GetMonitoringApi();

    /// <summary>
    /// Transitions the specified published message rows to the <c>Delayed</c> state for deferred dispatch.
    /// </summary>
    /// <param name="storageIds">The storage row identifiers of the messages to transition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask ChangePublishStateToDelayedAsync(Guid[] storageIds, CancellationToken cancellationToken = default);

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
    /// <param name="transaction">Optional ambient transaction (<see cref="System.Data.Common.DbTransaction"/> or an EF Core <c>IDbContextTransaction</c>).</param>
    /// <param name="nextRetryAt">
    /// UTC timestamp at which the retry processor should re-dispatch this message.
    /// Must be UTC — non-UTC values are provider-normalized. Pass <see langword="null"/> to clear
    /// the persisted column. Only retry-transition paths pass a value.
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
        object? transaction = null,
        DateTimeOffset? nextRetryAt = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Updates published retry state with optimistic checks for both durable retry counters.</summary>
    ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically reserves the next published delivery attempt under the active lease.</summary>
    ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
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
    /// <param name="nextRetryAt">
    /// UTC timestamp at which the retry processor should re-dispatch this message.
    /// Must be UTC — non-UTC values are provider-normalized. Pass <see langword="null"/> to clear
    /// the persisted column. Only retry-transition paths pass a value.
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
        DateTimeOffset? nextRetryAt = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Updates received retry state with optimistic checks for both durable retry counters.</summary>
    ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically reserves the next received delivery attempt under the active lease.</summary>
    ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
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
    /// Optional ambient transaction (<see cref="System.Data.Common.DbTransaction"/> or an EF Core
    /// <c>IDbContextTransaction</c>). When supplied, the insert participates in the caller's transaction.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored envelope with <c>StorageId</c> populated by the storage provider.</returns>
    ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        object? transaction = null,
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
        object? transaction = null,
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
                IntentType = IntentType.Bus,
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
    /// <param name="group">The consumer group that received the message.</param>
    /// <param name="message">The failed received message envelope, including serialized content and delivery intent.</param>
    /// <param name="exceptionInfo">Optional serialized exception details to persist alongside the failed row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the row was stored; <see langword="false"/> when storage was skipped or failed.</returns>
    ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores a failed received message using raw serialized content when no <c>MediumMessage</c> envelope is available.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) as received from the broker.</param>
    /// <param name="group">The consumer group that received the message.</param>
    /// <param name="content">The raw serialized message body.</param>
    /// <param name="exceptionInfo">Optional serialized exception details to persist alongside the failed row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the row was stored; <see langword="false"/> when storage was skipped or failed.</returns>
    ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists an inbound message envelope to the received table and returns the stored row with its assigned <c>StorageId</c>.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) as received from the broker.</param>
    /// <param name="group">The consumer group that received the message.</param>
    /// <param name="message">The received message envelope to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored envelope with <c>StorageId</c> populated by the storage provider.</returns>
    ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        MediumMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists an inbound <see cref="Message"/> to the received table, wrapping it in a
    /// <c>MediumMessage</c> envelope before delegating to the primary overload.
    /// </summary>
    /// <param name="name">The message name (topic or queue name) as received from the broker.</param>
    /// <param name="group">The consumer group that received the message.</param>
    /// <param name="content">The raw message to wrap and persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored envelope with <c>StorageId</c> populated by the storage provider.</returns>
    ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message content,
        CancellationToken cancellationToken = default
    )
    {
        return StoreReceivedMessageAsync(
            name,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = content,
                Content = string.Empty,
                IntentType = IntentType.Bus,
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Deletes expired message rows from the specified table in bounded batches.
    /// </summary>
    /// <param name="table">The physical table name to clean (use <c>IStorageInitializer.GetPublishedTableName()</c> or <c>GetReceivedTableName()</c>).</param>
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
    /// Returns published messages due for retry, filtered by <c>NextRetryAt &lt;= now()</c> using the
    /// injected <see cref="TimeProvider"/> that created the schedule.
    /// No lookback window is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Atomic claim-and-return:</b> returned rows are already leased — the same statement that
    /// selects them advances <c>LockedUntil</c> to <c>now + RetryPolicyOptions.DispatchTimeout</c>. Relational
    /// providers use their database clock for lease-expiry comparison and stamping while retaining the injected
    /// <see cref="TimeProvider"/> as the <c>NextRetryAt</c> scheduling authority. In-memory providers use their
    /// injected <see cref="TimeProvider"/> for both responsibilities.
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
        Func<object?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns received messages due for retry, filtered by <c>NextRetryAt &lt;= now()</c> using the
    /// injected <see cref="TimeProvider"/> that created the schedule.
    /// No lookback window is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Atomic claim-and-return:</b> returned rows are already leased — the same statement that
    /// selects them advances <c>LockedUntil</c> to <c>now + RetryPolicyOptions.DispatchTimeout</c>. Relational
    /// providers use their database clock for lease-expiry comparison and stamping while retaining the injected
    /// <see cref="TimeProvider"/> as the <c>NextRetryAt</c> scheduling authority. In-memory providers use their
    /// injected <see cref="TimeProvider"/> for both responsibilities.
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
    /// </remarks>
    ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
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
}
