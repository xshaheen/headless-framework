// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Persistence;

[PublicAPI]
public interface IDataStorage
{
    // Dashboard api
    IMonitoringApi GetMonitoringApi();

    ValueTask<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    );

    ValueTask ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default);

    ValueTask RenewLockAsync(string key, TimeSpan ttl, string instance, CancellationToken cancellationToken = default);

    ValueTask ChangePublishStateToDelayedAsync(long[] storageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status of a published message in storage.
    /// </summary>
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
    /// pickup-eligible again. Only retry-transition or in-flight paths supply a value, and the
    /// pre-attempt lease itself is written via <see cref="LeasePublishAsync"/>.
    /// </param>
    /// <param name="originalRetries">
    /// Optimistic-concurrency token. When supplied, storage applies <c>AND Retries = @OriginalRetries</c>
    /// to the conditional update so a concurrent replica's advance cannot be silently overwritten.
    /// Pass <see langword="null"/> on non-retry transitions where counter-race protection is unneeded.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the row was updated; <c>false</c> when the row was already in a terminal state
    /// (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled retry),
    /// the row was not found, or the optimistic <paramref name="originalRetries"/> predicate did not match.
    /// </returns>
    ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? transaction = null,
        DateTime? nextRetryAt = null,
        DateTime? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes a pre-attempt lease on a published message, setting <c>LockedUntil</c> on the row so
    /// the persisted retry processor excludes it while a dispatch attempt is active.
    /// </summary>
    /// <param name="message">The message to lease. On success the caller's <c>LockedUntil</c> is also updated.</param>
    /// <param name="lockedUntil">UTC timestamp at which the lease expires.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the lease was written; <c>false</c> when the row was already in a terminal
    /// state (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled
    /// retry) or the row was not found. Callers must stop the attempt path on <c>false</c>.
    /// </returns>
    ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        DateTime lockedUntil,
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
    /// UTC timestamp until which the row is leased by an active consume attempt. Pass
    /// <see langword="null"/> on terminal and retry-schedule writes so the row becomes
    /// pickup-eligible again. The pre-attempt lease itself is written via <see cref="LeaseReceiveAsync"/>.
    /// </param>
    /// <param name="originalRetries">
    /// Optimistic-concurrency token. When supplied, storage applies <c>AND Retries = @OriginalRetries</c>
    /// to the conditional update so a concurrent replica's advance cannot be silently overwritten.
    /// Pass <see langword="null"/> on non-retry transitions where counter-race protection is unneeded.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the row was updated; <c>false</c> when the row was already in a terminal state
    /// (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled retry),
    /// the row was not found, or the optimistic <paramref name="originalRetries"/> predicate did not match.
    /// </returns>
    ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt = null,
        DateTime? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes a pre-attempt lease on a received message, setting <c>LockedUntil</c> on the row so
    /// the persisted retry processor excludes it while a consume attempt is active.
    /// </summary>
    /// <param name="message">The message to lease. On success the caller's <c>LockedUntil</c> is also updated.</param>
    /// <param name="lockedUntil">UTC timestamp at which the lease expires.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the lease was written; <c>false</c> when the row was already in a terminal
    /// state (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled
    /// retry) or the row was not found. Callers must stop the attempt path on <c>false</c>.
    /// </returns>
    ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    );

    ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? transaction = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores a failed received message using serialized <see cref="Message"/> JSON so providers can persist message headers.
    /// </summary>
    /// <param name="content">Serialized <see cref="Message"/> payload, including headers.</param>
    ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    );

    ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message content,
        CancellationToken cancellationToken = default
    );

    ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTime timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns published messages due for retry, filtered by <c>NextRetryAt &lt;= now()</c>.
    /// No lookback window is applied.
    /// </summary>
    ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    );

    ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns received messages due for retry, filtered by <c>NextRetryAt &lt;= now()</c>.
    /// No lookback window is applied.
    /// </summary>
    ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    );

    ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default);
}
