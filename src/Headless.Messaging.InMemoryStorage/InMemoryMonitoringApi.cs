// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Primitives;

namespace Headless.Messaging.InMemoryStorage;

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

    public ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _GetHourlyTimelineStats(type, nameof(StatusName.Failed));
    }

    public ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _GetHourlyTimelineStats(type, nameof(StatusName.Succeeded));
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

    public ValueTask<long> PublishedFailedCount(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Failed));
    }

    public ValueTask<long> PublishedSucceededCount(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded));
    }

    public ValueTask<long> ReceivedFailedCount(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Failed));
    }

    public ValueTask<long> ReceivedSucceededCount(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(storage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded));
    }

    private ValueTask<Dictionary<DateTime, int>> _GetHourlyTimelineStats(MessageType type, string statusName)
    {
        // Hourly buckets are label keys, not persisted instants.
        var endDate = timeProvider.GetUtcNow().UtcDateTime;
        var dates = new List<DateTime>();
        for (var i = 0; i < 24; i++)
        {
            dates.Add(endDate);
            endDate = endDate.AddHours(-1);
        }

        var keyMaps = dates.ToDictionary(
            x => x.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture),
            x => x,
            StringComparer.Ordinal
        );

        var valuesMap =
            type == MessageType.Publish
                ? storage
                    .PublishedMessages.Values.Where(x =>
                        string.Equals(x.StatusName.ToString(), statusName, StringComparison.Ordinal)
                    )
                    .GroupBy(
                        x => x.Added.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture),
                        StringComparer.Ordinal
                    )
                    .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal)
                : storage
                    .ReceivedMessages.Values.Where(x =>
                        string.Equals(x.StatusName.ToString(), statusName, StringComparison.Ordinal)
                    )
                    .GroupBy(
                        x => x.Added.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture),
                        StringComparer.Ordinal
                    )
                    .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

        foreach (var key in keyMaps.Keys)
        {
            valuesMap.TryAdd(key, 0);
        }

        var result = new Dictionary<DateTime, int>();

        for (var i = 0; i < keyMaps.Count; i++)
        {
            var value = valuesMap[keyMaps.ElementAt(i).Key];
            result.Add(keyMaps.ElementAt(i).Value, value);
        }

        return ValueTask.FromResult(result);
    }
}
