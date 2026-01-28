// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.InMemoryStorage;

internal class InMemoryDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    ISerializer serializer,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider
) : IDataStorage
{
    public static ConcurrentDictionary<string, MemoryMessage> PublishedMessages { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, MemoryMessage> ReceivedMessages { get; } = new(StringComparer.Ordinal);

    public ValueTask<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(true);
    }

    public ValueTask ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask RenewLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ChangePublishStateToDelayedAsync(string[] ids, CancellationToken cancellationToken = default)
    {
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishedMessages[id].StatusName = StatusName.Delayed;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? dbTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        PublishedMessages[message.DbId].StatusName = state;
        PublishedMessages[message.DbId].ExpiresAt = message.ExpiresAt;
        PublishedMessages[message.DbId].Content = serializer.Serialize(message.Origin);
        return ValueTask.CompletedTask;
    }

    public ValueTask ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceivedMessages[message.DbId].StatusName = state;
        ReceivedMessages[message.DbId].ExpiresAt = message.ExpiresAt;
        ReceivedMessages[message.DbId].Content = serializer.Serialize(message.Origin);
        return ValueTask.CompletedTask;
    }

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? dbTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        return ValueTask.FromResult(message);
    }

    public ValueTask StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        ReceivedMessages[id] = new MemoryMessage
        {
            DbId = id,
            Group = group,
            Origin = null!,
            Name = name,
            Content = content,
            Retries = messagingOptions.Value.FailedRetryCount,
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = timeProvider
                .GetUtcNow()
                .UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter),
            StatusName = StatusName.Failed,
        };

        return ValueTask.CompletedTask;
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        return ValueTask.FromResult(mdMessage);
    }

    public ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTime timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var removed = 0;
        if (string.Equals(table, nameof(PublishedMessages), StringComparison.Ordinal))
        {
            var ids = PublishedMessages.Values.Where(x => x.ExpiresAt < timeout).Select(x => x.DbId).Take(batchCount);

            removed += ids.Count(id => PublishedMessages.TryRemove(id, out _));
        }
        else
        {
            var ids = ReceivedMessages.Values.Where(x => x.ExpiresAt < timeout).Select(x => x.DbId).Take(batchCount);

            removed += ids.Count(id => ReceivedMessages.TryRemove(id, out _));
        }

        return ValueTask.FromResult(removed);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<MediumMessage> result = PublishedMessages
            .Values.Where(x =>
                x.Retries < messagingOptions.Value.FailedRetryCount
                && x.Added < timeProvider.GetUtcNow().UtcDateTime.Subtract(lookbackSeconds)
                && (x.StatusName == StatusName.Scheduled || x.StatusName == StatusName.Failed)
            )
            .Take(200)
            .Cast<MediumMessage>()
            .ToList();

        //foreach (var message in result)
        //{
        //    message.Origin = _serializer.DeserializeAsync(message.Content)!;
        //}

        return ValueTask.FromResult(result);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<MediumMessage> result = ReceivedMessages
            .Values.Where(x =>
                x.Retries < messagingOptions.Value.FailedRetryCount
                && x.Added < timeProvider.GetUtcNow().UtcDateTime.Subtract(lookbackSeconds)
                && (x.StatusName == StatusName.Scheduled || x.StatusName == StatusName.Failed)
            )
            .Take(200)
            .Select(x => (MediumMessage)x)
            .ToList();

        return ValueTask.FromResult(result);
    }

    public ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleteResult = ReceivedMessages.TryRemove(id.ToString(CultureInfo.InvariantCulture), out _);
        return ValueTask.FromResult(deleteResult ? 1 : 0);
    }

    public ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleteResult = PublishedMessages.TryRemove(id.ToString(CultureInfo.InvariantCulture), out _);
        return ValueTask.FromResult(deleteResult ? 1 : 0);
    }

    public ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = PublishedMessages
            .Values.Where(x =>
                (x.StatusName == StatusName.Delayed && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2))
                || (
                    x.StatusName == StatusName.Queued
                    && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1)
                )
            )
            .Take(messagingOptions.Value.SchedulerBatchSize)
            .Cast<MediumMessage>();

        return scheduleTask(null!, result);
    }

    public IMonitoringApi GetMonitoringApi()
    {
        return new InMemoryMonitoringApi(timeProvider);
    }
}
