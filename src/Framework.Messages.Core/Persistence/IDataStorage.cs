// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;

namespace Framework.Messages.Persistence;

public interface IDataStorage
{
    Task<bool> AcquireLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default);

    Task ReleaseLockAsync(string key, string instance, CancellationToken token = default);

    Task RenewLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default);

    Task ChangePublishStateToDelayedAsync(string[] ids);

    Task ChangePublishStateAsync(MediumMessage message, StatusName state, object? transaction = null);

    Task ChangeReceiveStateAsync(MediumMessage message, StatusName state);

    Task<MediumMessage> StoreMessageAsync(string name, Message content, object? transaction = null);

    Task StoreReceivedExceptionMessageAsync(string name, string group, string content);

    Task<MediumMessage> StoreReceivedMessageAsync(string name, string group, Message content);

    Task<int> DeleteExpiresAsync(
        string table,
        DateTime timeout,
        int batchCount = 1000,
        CancellationToken token = default
    );

    Task<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(TimeSpan lookbackSeconds);

    Task ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, Task> scheduleTask,
        CancellationToken token = default
    );

    Task<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(TimeSpan lookbackSeconds);

    Task<int> DeleteReceivedMessageAsync(long id);

    Task<int> DeletePublishedMessageAsync(long id);

    //dashboard api
    IMonitoringApi GetMonitoringApi();
}
