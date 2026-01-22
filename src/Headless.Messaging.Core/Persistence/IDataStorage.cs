// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Persistence;

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

    ValueTask ChangePublishStateToDelayedAsync(string[] ids, CancellationToken cancellationToken = default);

    ValueTask ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? transaction = null,
        CancellationToken cancellationToken = default
    );

    ValueTask ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        CancellationToken cancellationToken = default
    );

    ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? transaction = null,
        CancellationToken cancellationToken = default
    );

    ValueTask StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
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

    ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    );

    ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    );

    ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    );

    ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default);
}
