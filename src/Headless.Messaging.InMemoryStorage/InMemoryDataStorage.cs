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

internal sealed class InMemoryDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    ISerializer serializer,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider
) : IDataStorage
{
    public ConcurrentDictionary<long, MemoryMessage> PublishedMessages { get; } = new();

    public ConcurrentDictionary<long, MemoryMessage> ReceivedMessages { get; } = new();

    internal ConcurrentDictionary<string, (string Instance, DateTime ExpiresAt)> Locks { get; } =
        new(StringComparer.Ordinal);

    public void Clear()
    {
        PublishedMessages.Clear();
        ReceivedMessages.Clear();
        Locks.Clear();
    }

    public ValueTask<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(ttl);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Locks.TryGetValue(key, out var current))
            {
                if (Locks.TryAdd(key, (instance, expiresAt)))
                {
                    return ValueTask.FromResult(true);
                }

                continue;
            }

            if (current.ExpiresAt > now)
            {
                return ValueTask.FromResult(false);
            }

            if (Locks.TryUpdate(key, (instance, expiresAt), current))
            {
                return ValueTask.FromResult(true);
            }
        }
    }

    public ValueTask ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (Locks.TryGetValue(key, out var current))
        {
            if (!string.Equals(current.Instance, instance, StringComparison.Ordinal))
            {
                return ValueTask.CompletedTask;
            }

            if (Locks.TryUpdate(key, (string.Empty, DateTime.MinValue), current))
            {
                return ValueTask.CompletedTask;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RenewLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (Locks.TryGetValue(key, out var current))
        {
            if (!string.Equals(current.Instance, instance, StringComparison.Ordinal))
            {
                return ValueTask.CompletedTask;
            }

            if (Locks.TryUpdate(key, (instance, current.ExpiresAt.Add(ttl)), current))
            {
                return ValueTask.CompletedTask;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ChangePublishStateToDelayedAsync(long[] storageIds, CancellationToken cancellationToken = default)
    {
        foreach (var storageId in storageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishedMessages[storageId].StatusName = StatusName.Delayed;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? dbTransaction = null,
        DateTime? nextRetryAt = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var utcNextRetryAt = nextRetryAt.ToUtcOrSelf();
        PublishedMessages[message.StorageId].StatusName = state;
        PublishedMessages[message.StorageId].ExpiresAt = message.ExpiresAt;
        PublishedMessages[message.StorageId].NextRetryAt = utcNextRetryAt;
        PublishedMessages[message.StorageId].Retries = message.Retries;
        PublishedMessages[message.StorageId].Content = serializer.Serialize(message.Origin);
        return ValueTask.CompletedTask;
    }

    public ValueTask ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var utcNextRetryAt = nextRetryAt.ToUtcOrSelf();
        ReceivedMessages[message.StorageId].StatusName = state;
        ReceivedMessages[message.StorageId].ExpiresAt = message.ExpiresAt;
        ReceivedMessages[message.StorageId].NextRetryAt = utcNextRetryAt;
        ReceivedMessages[message.StorageId].Retries = message.Retries;
        ReceivedMessages[message.StorageId].Content = serializer.Serialize(message.Origin);
        ReceivedMessages[message.StorageId].ExceptionInfo = message.ExceptionInfo;
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
            StorageId = longIdGenerator.Create(),
            Origin = content,
            Content = serializer.Serialize(content),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
            NextRetryAt = null,
            Retries = 0,
        };

        PublishedMessages[message.StorageId] = new MemoryMessage
        {
            StorageId = message.StorageId,
            Name = name,
            Origin = message.Origin,
            Content = message.Content,
            Retries = message.Retries,
            Added = message.Added,
            ExpiresAt = message.ExpiresAt,
            NextRetryAt = message.NextRetryAt,
            StatusName = StatusName.Scheduled,
        };

        return ValueTask.FromResult(message);
    }

    public ValueTask StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = longIdGenerator.Create();

        ReceivedMessages[id] = new MemoryMessage
        {
            StorageId = id,
            Group = group,
            Origin =
                serializer.Deserialize(content)
                ?? throw new InvalidOperationException("Failed to deserialize received exception message content."),
            Name = name,
            Content = content,
            Retries = messagingOptions.Value.RetryPolicy.MaxAttempts,
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = timeProvider
                .GetUtcNow()
                .UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter),
            NextRetryAt = null,
            StatusName = StatusName.Failed,
            ExceptionInfo = exceptionInfo,
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
            StorageId = longIdGenerator.Create(),
            Origin = message,
            Content = serializer.Serialize(message),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
            NextRetryAt = null,
            Retries = 0,
        };

        ReceivedMessages[mdMessage.StorageId] = new MemoryMessage
        {
            StorageId = mdMessage.StorageId,
            Origin = mdMessage.Origin,
            Group = group,
            Name = name,
            Content = mdMessage.Content,
            Retries = mdMessage.Retries,
            Added = mdMessage.Added,
            ExpiresAt = mdMessage.ExpiresAt,
            NextRetryAt = mdMessage.NextRetryAt,
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
            var ids = PublishedMessages
                .Values.Where(x =>
                    x.ExpiresAt < timeout && (x.StatusName == StatusName.Succeeded || x.StatusName == StatusName.Failed)
                )
                .Select(x => x.StorageId)
                .Take(batchCount);

            removed += ids.Count(id => PublishedMessages.TryRemove(id, out _));
        }
        else
        {
            var ids = ReceivedMessages
                .Values.Where(x =>
                    x.ExpiresAt < timeout && (x.StatusName == StatusName.Succeeded || x.StatusName == StatusName.Failed)
                )
                .Select(x => x.StorageId)
                .Take(batchCount);

            removed += ids.Count(id => ReceivedMessages.TryRemove(id, out _));
        }

        return ValueTask.FromResult(removed);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxAttempts = messagingOptions.Value.RetryPolicy.MaxAttempts;
        IEnumerable<MediumMessage> result = PublishedMessages
            .Values.Where(x =>
                x.Retries < maxAttempts
                && (
                    (x.NextRetryAt is not null && x.NextRetryAt <= now)
                    || (x.StatusName == StatusName.Scheduled && x.NextRetryAt is null)
                )
            )
            .Take(200)
            .Cast<MediumMessage>()
            .ToList();

        return ValueTask.FromResult(result);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxAttempts = messagingOptions.Value.RetryPolicy.MaxAttempts;
        IEnumerable<MediumMessage> result = ReceivedMessages
            .Values.Where(x =>
                x.Retries < maxAttempts
                && (
                    (x.NextRetryAt is not null && x.NextRetryAt <= now)
                    || (x.StatusName == StatusName.Scheduled && x.NextRetryAt is null)
                )
            )
            .Take(200)
            .Select(x => (MediumMessage)x)
            .ToList();

        return ValueTask.FromResult(result);
    }

    public ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleteResult = ReceivedMessages.TryRemove(id, out _);
        return ValueTask.FromResult(deleteResult ? 1 : 0);
    }

    public ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleteResult = PublishedMessages.TryRemove(id, out _);
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
        return new InMemoryMonitoringApi(this, timeProvider);
    }
}
