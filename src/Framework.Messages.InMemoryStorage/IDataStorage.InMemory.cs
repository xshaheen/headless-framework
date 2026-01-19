// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Framework.Abstractions;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

#pragma warning disable MA0049
internal class InMemoryStorage(
    IOptions<CapOptions> capOptions,
    ISerializer serializer,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider
)
#pragma warning restore MA0049
    : IDataStorage
{
    public static ConcurrentDictionary<string, MemoryMessage> PublishedMessages { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, MemoryMessage> ReceivedMessages { get; } = new(StringComparer.Ordinal);

    public Task<bool> AcquireLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default)
    {
        return Task.FromResult(true);
    }

    public Task ReleaseLockAsync(string key, string instance, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public Task RenewLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public Task ChangePublishStateToDelayedAsync(string[] ids)
    {
        foreach (var id in ids)
        {
            PublishedMessages[id].StatusName = StatusName.Delayed;
        }

        return Task.CompletedTask;
    }

    public Task ChangePublishStateAsync(MediumMessage message, StatusName state, object? dbTransaction = null)
    {
        PublishedMessages[message.DbId].StatusName = state;
        PublishedMessages[message.DbId].ExpiresAt = message.ExpiresAt;
        PublishedMessages[message.DbId].Content = serializer.Serialize(message.Origin);
        return Task.CompletedTask;
    }

    public Task ChangeReceiveStateAsync(MediumMessage message, StatusName state)
    {
        ReceivedMessages[message.DbId].StatusName = state;
        ReceivedMessages[message.DbId].ExpiresAt = message.ExpiresAt;
        ReceivedMessages[message.DbId].Content = serializer.Serialize(message.Origin);
        return Task.CompletedTask;
    }

    public Task<MediumMessage> StoreMessageAsync(string name, Message content, object? dbTransaction = null)
    {
        var message = new MediumMessage
        {
            DbId = content.GetId(),
            Origin = content,
            Content = serializer.Serialize(content),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
            Retries = 0,
        };

        PublishedMessages[message.DbId] = new MemoryMessage
        {
            DbId = message.DbId,
            Name = name,
            Origin = message.Origin,
            Content = message.Content,
            Retries = message.Retries,
            Added = message.Added,
            ExpiresAt = message.ExpiresAt,
            StatusName = StatusName.Scheduled,
        };

        return Task.FromResult(message);
    }

    public Task StoreReceivedExceptionMessageAsync(string name, string group, string content)
    {
        var id = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        ReceivedMessages[id] = new MemoryMessage
        {
            DbId = id,
            Group = group,
            Origin = null!,
            Name = name,
            Content = content,
            Retries = capOptions.Value.FailedRetryCount,
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(capOptions.Value.FailedMessageExpiredAfter),
            StatusName = StatusName.Failed,
        };

        return Task.CompletedTask;
    }

    public Task<MediumMessage> StoreReceivedMessageAsync(string name, string group, Message message)
    {
        var mdMessage = new MediumMessage
        {
            DbId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture),
            Origin = message,
            Content = serializer.Serialize(message),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
            Retries = 0,
        };

        ReceivedMessages[mdMessage.DbId] = new MemoryMessage
        {
            DbId = mdMessage.DbId,
            Origin = mdMessage.Origin,
            Group = group,
            Name = name,
            Content = mdMessage.Content,
            Retries = mdMessage.Retries,
            Added = mdMessage.Added,
            ExpiresAt = mdMessage.ExpiresAt,
            StatusName = StatusName.Scheduled,
        };

        return Task.FromResult(mdMessage);
    }

    public Task<int> DeleteExpiresAsync(
        string table,
        DateTime timeout,
        int batchCount = 1000,
        CancellationToken token = default
    )
    {
        var removed = 0;
        if (table == nameof(PublishedMessages))
        {
            var ids = PublishedMessages.Values.Where(x => x.ExpiresAt < timeout).Select(x => x.DbId).Take(batchCount);

            foreach (var id in ids)
            {
                if (PublishedMessages.TryRemove(id, out _))
                    removed++;
            }
        }
        else
        {
            var ids = ReceivedMessages.Values.Where(x => x.ExpiresAt < timeout).Select(x => x.DbId).Take(batchCount);

            foreach (var id in ids)
            {
                if (ReceivedMessages.TryRemove(id, out _))
                    removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(TimeSpan lookbackSeconds)
    {
        IEnumerable<MediumMessage> result = PublishedMessages
            .Values.Where(x =>
                x.Retries < capOptions.Value.FailedRetryCount
                && x.Added < timeProvider.GetUtcNow().UtcDateTime.Subtract(lookbackSeconds)
                && (x.StatusName == StatusName.Scheduled || x.StatusName == StatusName.Failed)
            )
            .Take(200)
            .Select(x => (MediumMessage)x)
            .ToList();

        //foreach (var message in result)
        //{
        //    message.Origin = _serializer.DeserializeAsync(message.Content)!;
        //}

        return Task.FromResult(result);
    }

    public Task<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(TimeSpan lookbackSeconds)
    {
        IEnumerable<MediumMessage> result = ReceivedMessages
            .Values.Where(x =>
                x.Retries < capOptions.Value.FailedRetryCount
                && x.Added < timeProvider.GetUtcNow().UtcDateTime.Subtract(lookbackSeconds)
                && (x.StatusName == StatusName.Scheduled || x.StatusName == StatusName.Failed)
            )
            .Take(200)
            .Select(x => (MediumMessage)x)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int> DeleteReceivedMessageAsync(long id)
    {
        var deleteResult = ReceivedMessages.TryRemove(id.ToString(CultureInfo.InvariantCulture), out _);
        return Task.FromResult(deleteResult ? 1 : 0);
    }

    public Task<int> DeletePublishedMessageAsync(long id)
    {
        var deleteResult = PublishedMessages.TryRemove(id.ToString(CultureInfo.InvariantCulture), out _);
        return Task.FromResult(deleteResult ? 1 : 0);
    }

    public Task ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, Task> scheduleTask,
        CancellationToken token = default
    )
    {
        var result = PublishedMessages
            .Values.Where(x =>
                (x.StatusName == StatusName.Delayed && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2))
                || (
                    x.StatusName == StatusName.Queued
                    && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1)
                )
            )
            .Take(capOptions.Value.SchedulerBatchSize)
            .Select(x => (MediumMessage)x);

        return scheduleTask(null!, result);
    }

    public IMonitoringApi GetMonitoringApi()
    {
        return new InMemoryMonitoringApi(timeProvider);
    }
}
