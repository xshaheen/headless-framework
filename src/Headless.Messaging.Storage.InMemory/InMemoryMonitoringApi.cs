// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Primitives;

namespace Headless.Messaging.Storage.InMemory;

internal sealed class InMemoryMonitoringApi(InMemoryDataStorage storage, TimeProvider timeProvider) : IMonitoringApi
{
    public ValueTask<MediumMessage?> GetPublishedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            storage.PublishedMessages.TryGetValue(id, out var message) ? (MediumMessage?)message : null
        );
    }

    public ValueTask<IReadOnlyList<MediumMessage>> GetPublishedMessagesAsync(
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<MediumMessage>(storageIds.Count);

        foreach (var id in storageIds)
        {
            if (storage.PublishedMessages.TryGetValue(id, out var message))
            {
                result.Add(message);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<MediumMessage>>(result);
    }

    public ValueTask<MediumMessage?> GetReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            storage.ReceivedMessages.TryGetValue(id, out var message) ? (MediumMessage?)message : null
        );
    }

    public ValueTask<IReadOnlyList<MediumMessage>> GetReceivedMessagesAsync(
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<MediumMessage>(storageIds.Count);

        foreach (var id in storageIds)
        {
            if (storage.ReceivedMessages.TryGetValue(id, out var message))
            {
                result.Add(message);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<MediumMessage>>(result);
    }

    public ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int publishedSucceeded = 0;
        int publishedFailed = 0;
        int publishedDelayed = 0;
        int publishedPendingRetry = 0;

        // Single pass over each value collection — the original implementation enumerated the
        // dictionary up to 4× per side; this collapses it to 1×.
        foreach (var msg in storage.PublishedMessages.Values)
        {
            switch (msg.StatusName)
            {
                case StatusName.Succeeded:
                    publishedSucceeded++;
                    break;
                case StatusName.Failed:
                    publishedFailed++;
                    break;
                case StatusName.Delayed:
                    publishedDelayed++;
                    break;
            }

            if (msg.NextRetryAt is not null)
            {
                publishedPendingRetry++;
            }
        }

        int receivedSucceeded = 0;
        int receivedFailed = 0;
        int receivedPendingRetry = 0;

        foreach (var msg in storage.ReceivedMessages.Values)
        {
            switch (msg.StatusName)
            {
                case StatusName.Succeeded:
                    receivedSucceeded++;
                    break;
                case StatusName.Failed:
                    receivedFailed++;
                    break;
            }

            if (msg.NextRetryAt is not null)
            {
                receivedPendingRetry++;
            }
        }

        var stats = new StatisticsView
        {
            PublishedSucceeded = publishedSucceeded,
            ReceivedSucceeded = receivedSucceeded,
            PublishedFailed = publishedFailed,
            ReceivedFailed = receivedFailed,
            PublishedDelayed = publishedDelayed,
            PublishedPendingRetry = publishedPendingRetry,
            ReceivedPendingRetry = receivedPendingRetry,
        };

        return ValueTask.FromResult(stats);
    }

    public ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlyFailedJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _GetHourlyTimelineStats(type, StatusName.Failed);
    }

    public ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlySucceededJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _GetHourlyTimelineStats(type, StatusName.Succeeded);
    }

    public ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (query.MessageType == MessageType.Publish)
        {
            var expression = storage.PublishedMessages.Values.Where(x => true);

            if (query.StatusName is not null)
            {
                expression = expression.Where(x => x.StatusName == query.StatusName);
            }

            if (!string.IsNullOrEmpty(query.Name))
            {
                expression = expression.Where(x => x.Name.Equals(query.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(query.Content))
            {
                expression = expression.Where(x => x.Content.Contains(query.Content, StringComparison.Ordinal));
            }

            if (query.IntentType is { } intentType)
            {
                expression = expression.Where(x => x.IntentType == intentType);
            }

            var offset = query.CurrentPage * query.PageSize;
            var size = query.PageSize;

            // Materialize the filtered list once, then skip/take to project only the requested
            // page — avoids allocating MessageView instances for the rows we discard.
            var filtered = expression.ToList();
            var pageItems = filtered
                .Skip(offset)
                .Take(size)
                .Select(x => new MessageView
                {
                    Added = x.Added,
                    StorageId = x.StorageId,
                    MessageId = x.Origin.Id,
                    Version = "N/A",
                    Content = x.Content,
                    IntentType = x.IntentType,
                    ExpiresAt = x.ExpiresAt,
                    Name = x.Name,
                    Retries = x.Retries,
                    StatusName = x.StatusName,
                    NextRetryAt = x.NextRetryAt,
                    LockedUntil = x.LockedUntil,
                })
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<MessageView>(pageItems, query.CurrentPage, query.PageSize, filtered.Count)
            );
        }
        else
        {
            var expression = storage.ReceivedMessages.Values.Where(x => true);

            if (query.StatusName is not null)
            {
                expression = expression.Where(x => x.StatusName == query.StatusName);
            }

            if (!string.IsNullOrEmpty(query.Name))
            {
                expression = expression.Where(x => x.Name.Equals(query.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(query.Group))
            {
                expression = expression.Where(x =>
                    x.Group?.Equals(query.Group, StringComparison.OrdinalIgnoreCase) == true
                );
            }

            if (!string.IsNullOrEmpty(query.Content))
            {
                expression = expression.Where(x => x.Content.Contains(query.Content, StringComparison.Ordinal));
            }

            if (query.IntentType is { } intentType)
            {
                expression = expression.Where(x => x.IntentType == intentType);
            }

            var offset = query.CurrentPage * query.PageSize;
            var size = query.PageSize;

            var filtered = expression.ToList();
            var pageItems = filtered
                .Skip(offset)
                .Take(size)
                .Select(x => new MessageView
                {
                    Added = x.Added,
                    Group = x.Group,
                    StorageId = x.StorageId,
                    MessageId = x.Origin.Id,
                    Version = "N/A",
                    Content = x.Content,
                    IntentType = x.IntentType,
                    ExpiresAt = x.ExpiresAt,
                    Name = x.Name,
                    Retries = x.Retries,
                    StatusName = x.StatusName,
                    NextRetryAt = x.NextRetryAt,
                    LockedUntil = x.LockedUntil,
                })
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<MessageView>(pageItems, query.CurrentPage, query.PageSize, filtered.Count)
            );
        }
    }

    public ValueTask<long> GetPublishedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Failed));
    }

    public ValueTask<long> GetPublishedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded));
    }

    public ValueTask<long> GetReceivedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Failed));
    }

    public ValueTask<long> GetReceivedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded));
    }

    private ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> _GetHourlyTimelineStats(
        MessageType type,
        StatusName statusName
    )
    {
        // Buckets cover the current UTC hour and the 23 preceding hours, keyed by hour start
        // (newest first, matching the SQL-backed monitoring implementations).
        var currentHour = timeProvider.GetUtcNow().TruncateToHours();
        var oldestHour = currentHour.AddHours(-23);

        var result = new Dictionary<DateTimeOffset, int>(capacity: 24);

        for (var i = 0; i < 24; i++)
        {
            result[currentHour.AddHours(-i)] = 0;
        }

        var messages = type == MessageType.Publish ? storage.PublishedMessages.Values : storage.ReceivedMessages.Values;

        foreach (var message in messages)
        {
            if (message.StatusName != statusName)
            {
                continue;
            }

            var bucket = message.Added.ToUniversalTime().TruncateToHours();

            if (bucket >= oldestHour && bucket <= currentHour)
            {
                result[bucket]++;
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<DateTimeOffset, int>>(result);
    }
}
