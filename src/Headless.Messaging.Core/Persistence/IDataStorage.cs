// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA1068 // Preserve existing optional-parameter call sites while adding concurrency metadata.

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
    /// <param name="transaction">Optional ambient transaction (DbTransaction or IDbContextTransaction).</param>
    /// <param name="nextRetryAt">
    /// UTC timestamp at which the retry processor should re-dispatch this message.
    /// Must be UTC — non-UTC values are provider-normalized. Pass <see langword="null"/> to clear
    /// the persisted column. Only retry-transition paths pass a value.
    /// </param>
    /// <param name="lockedUntil"></param>
    /// <param name="originalRetries"></param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the row was updated; <c>false</c> when the row was already in a terminal state
    /// (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled retry)
    /// or the row was not found.
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
    /// Leases a published message before a dispatch attempt.
    /// </summary>
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
    /// <param name="lockedUntil"></param>
    /// <param name="originalRetries"></param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the row was updated; <c>false</c> when the row was already in a terminal state
    /// (<see cref="StatusName.Failed"/> or <see cref="StatusName.Succeeded"/> with no scheduled retry)
    /// or the row was not found.
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
    /// Leases a received message before a consume attempt.
    /// </summary>
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

#pragma warning restore CA1068
