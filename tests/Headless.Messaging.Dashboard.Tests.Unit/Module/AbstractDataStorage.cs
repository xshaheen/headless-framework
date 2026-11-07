// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests.Module;

/// <summary>
/// Every abstract member of <see cref="IDataStorage"/> declared abstract, and none of its default members, so a
/// class substitute of it answers the members a test configures while the storage's own default members, such as
/// its change read, run unchanged on top of them.
/// </summary>
public abstract class AbstractDataStorage : IDataStorage
{
    public abstract IMonitoringApi GetMonitoringApi();

    public abstract IInboxOperationsApi GetInboxOperationsApi();

    public abstract ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<int> DeleteExpiredInboxAuditsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<int> DeleteExpiredInboxReceiptsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> IsInboxMessageAdmittedAsync(
        string? tenantId,
        string messageId,
        string messageName,
        string consumerIdentity,
        MessageLane lane,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> DeferReceivedInboxOrphanAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> ConfirmReceivedInboxRoutableAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<IEnumerable<MediumMessage>> GetReceivedInboxOrphansOfNeedRetryAsync(
        MessageLane lane,
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask ChangePublishStateToDelayedAsync(
        IReadOnlyCollection<MediumMessage> messages,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        DbTransaction? transaction = null,
        RetryDelay? retryDelay = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        RetryDelay? retryDelay = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<MediumMessage> StoreScheduledMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset publishAt,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTimeOffset timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        MessageLane lane,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<int> ReclaimDeadPublishedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask ScheduleMessagesOfDelayedAsync(
        Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        MessageLane lane,
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<int> ReclaimDeadReceivedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<int> DeleteReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default);

    public abstract ValueTask<int> DeleteReceivedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<int> DeletePublishedMessageAsync(Guid id, CancellationToken cancellationToken = default);

    public abstract ValueTask<int> DeletePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<IReadOnlyList<Guid>> RequeuePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<DateTimeOffset?> GetLatestMessageChangeMarkerAsync(
        CancellationToken cancellationToken = default
    );

    public abstract ValueTask<IReadOnlyList<MessageChange>> ReadChangedMessagesAsync(
        MessageChangeKind kind,
        DateTimeOffset after,
        DateTimeOffset through,
        MessageChangePosition? resumeAfter,
        int maxCount,
        CancellationToken cancellationToken = default
    );
}
