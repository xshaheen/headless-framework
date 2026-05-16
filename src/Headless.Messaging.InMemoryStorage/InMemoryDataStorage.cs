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

    // Serializes the lookup-then-insert/update path in StoreReceivedExceptionMessageAsync so
    // two concurrent broker redeliveries cannot both decide "not found" and race to insert
    // duplicate rows for the same (Version, MessageId, Group) tuple.
    private readonly Lock _receivedExceptionUpsertLock = new();

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

    public ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? dbTransaction = null,
        DateTime? nextRetryAt = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!PublishedMessages.TryGetValue(message.StorageId, out var current))
        {
            return ValueTask.FromResult(false);
        }

        bool updated;
        lock (current)
        {
            // Mirror the SQL providers' terminal guard: only reject when status is terminal AND
            // NextRetryAt is null. A Succeeded row with non-null NextRetryAt is degenerate but
            // shouldn't be blocked by this guard — cross-storage parity per the at-least-once contract.
            if (
                (current.StatusName is StatusName.Succeeded || current.StatusName is StatusName.Failed)
                && current.NextRetryAt is null
            )
            {
                return ValueTask.FromResult(false);
            }

            var utcNextRetryAt = nextRetryAt.ToUtcOrSelf();
            current.StatusName = state;
            current.ExpiresAt = message.ExpiresAt;
            current.NextRetryAt = utcNextRetryAt;
            current.Retries = message.Retries;
            current.Content = serializer.Serialize(message.Origin);
            updated = true;
        }

        return ValueTask.FromResult(updated);
    }

    public ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReceivedMessages.TryGetValue(message.StorageId, out var current))
        {
            return ValueTask.FromResult(false);
        }

        bool updated;
        lock (current)
        {
            // Mirror the SQL providers' terminal guard (see ChangePublishStateAsync above).
            if (
                (current.StatusName is StatusName.Succeeded || current.StatusName is StatusName.Failed)
                && current.NextRetryAt is null
            )
            {
                return ValueTask.FromResult(false);
            }

            var utcNextRetryAt = nextRetryAt.ToUtcOrSelf();
            current.StatusName = state;
            current.ExpiresAt = message.ExpiresAt;
            current.NextRetryAt = utcNextRetryAt;
            current.Retries = message.Retries;
            current.Content = serializer.Serialize(message.Origin);
            current.ExceptionInfo = message.ExceptionInfo;
            updated = true;
        }

        return ValueTask.FromResult(updated);
    }

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? dbTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var added = timeProvider.GetUtcNow().UtcDateTime;
        var message = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = content,
            Content = serializer.Serialize(content),
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
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
            Version = messagingOptions.Value.Version,
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

        var origin =
            serializer.Deserialize(content)
            ?? throw new InvalidOperationException("Failed to deserialize received exception message content.");

        var messageId = origin.GetId();
        var version = messagingOptions.Value.Version;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter);
        var retries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;

        // Upsert on (Version, MessageId, Group) — mirrors the SQL providers' MERGE / ON CONFLICT
        // semantics so broker redelivery doesn't accumulate duplicate rows. The terminal-row guard
        // also matches: a Succeeded/Failed entry with no scheduled retry is left alone so a
        // previously-succeeded row isn't overwritten back to Failed by a redelivery-then-deserialize-fail.
        // Lock the entire lookup-then-insert/update path so concurrent broker redeliveries cannot
        // both decide "not found" and race to insert duplicate rows.
        lock (_receivedExceptionUpsertLock)
        {
            var existing = ReceivedMessages.Values.FirstOrDefault(m =>
                string.Equals(m.Version, version, StringComparison.Ordinal)
                && string.Equals(m.Origin.GetId(), messageId, StringComparison.Ordinal)
                && string.Equals(m.Group, group, StringComparison.Ordinal)
            );

            if (existing is not null)
            {
                if (
                    (existing.StatusName is StatusName.Succeeded || existing.StatusName is StatusName.Failed)
                    && existing.NextRetryAt is null
                )
                {
                    // Terminal — leave it alone.
                    return ValueTask.CompletedTask;
                }

                existing.StatusName = StatusName.Failed;
                existing.Retries = retries;
                existing.ExpiresAt = expiresAt;
                existing.NextRetryAt = null;
                existing.Content = content;
                existing.ExceptionInfo = exceptionInfo;

                return ValueTask.CompletedTask;
            }

            var id = longIdGenerator.Create();
            ReceivedMessages[id] = new MemoryMessage
            {
                StorageId = id,
                Group = group,
                Origin = origin,
                Name = name,
                Content = content,
                Retries = retries,
                Added = now,
                ExpiresAt = expiresAt,
                NextRetryAt = null,
                StatusName = StatusName.Failed,
                ExceptionInfo = exceptionInfo,
                Version = version,
            };

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var added = timeProvider.GetUtcNow().UtcDateTime;
        var mdMessage = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = message,
            Content = serializer.Serialize(message),
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
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
            Version = messagingOptions.Value.Version,
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

    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxPersistedRetries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;
        var version = messagingOptions.Value.Version;
        // Return a snapshot (plain MediumMessage), not the live MemoryMessage reference, so that
        // pre-write caller mutations (ExceptionInfo, ExpiresAt, AddOrUpdateException on Origin) do
        // NOT leak into the dictionary entry when ChangeReceiveStateAsync's terminal guard rejects
        // the conditional UPDATE. The SQL providers naturally produce a snapshot because every column
        // comes back through deserialization; InMemory must do this explicitly.
        IEnumerable<MediumMessage> result = PublishedMessages
            .Values.Where(x =>
                string.Equals(x.Version, version, StringComparison.Ordinal)
                && x.Retries <= maxPersistedRetries
                && x.NextRetryAt is not null
                && x.NextRetryAt <= now
            )
            .Take(200)
            .Select(_ToSnapshot)
            .ToList();

        return ValueTask.FromResult(result);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxPersistedRetries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;
        var version = messagingOptions.Value.Version;
        IEnumerable<MediumMessage> result = ReceivedMessages
            .Values.Where(x =>
                string.Equals(x.Version, version, StringComparison.Ordinal)
                && x.Retries <= maxPersistedRetries
                && x.NextRetryAt is not null
                && x.NextRetryAt <= now
            )
            .Take(200)
            .Select(_ToSnapshot)
            .ToList();

        return ValueTask.FromResult(result);
    }

    private static MediumMessage _ToSnapshot(MemoryMessage m) =>
        new()
        {
            StorageId = m.StorageId,
            // Clone the Origin's Headers dictionary so caller mutations (e.g., AddOrUpdateException
            // before a write that the terminal-row guard then rejects) cannot leak back into the
            // stored Origin. Value is shared by reference — payload semantics treat it as immutable.
            Origin = new Message(
                new Dictionary<string, string?>(m.Origin.Headers, StringComparer.Ordinal),
                m.Origin.Value
            ),
            Content = m.Content,
            Added = m.Added,
            ExpiresAt = m.ExpiresAt,
            NextRetryAt = m.NextRetryAt,
            Retries = m.Retries,
            ExceptionInfo = m.ExceptionInfo,
        };

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
        var version = messagingOptions.Value.Version;
        var result = PublishedMessages
            .Values.Where(x =>
                string.Equals(x.Version, version, StringComparison.Ordinal)
                && (
                    (
                        x.StatusName == StatusName.Delayed
                        && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2)
                    )
                    || (
                        x.StatusName == StatusName.Queued
                        && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1)
                    )
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
