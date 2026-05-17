// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Primitives;

namespace Headless.Messaging.InMemoryStorage;

internal sealed class InMemoryMonitoringApi(InMemoryDataStorage storage, TimeProvider timeProvider) : IMonitoringApi
{
    public ValueTask<MediumMessage?> GetPublishedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            storage.PublishedMessages.TryGetValue(id, out var message) ? (MediumMessage?)message : null
        );
    }

    public ValueTask<MediumMessage?> GetReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            storage.ReceivedMessages.TryGetValue(id, out var message) ? (MediumMessage?)message : null
        );
    }

    public ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stats = new StatisticsView
        {
            PublishedSucceeded = storage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded),
            ReceivedSucceeded = storage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded),
            PublishedFailed = storage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Failed),
            ReceivedFailed = storage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Failed),
            PublishedDelayed = storage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Delayed),
            PublishedPendingRetry = storage.PublishedMessages.Values.Count(x => x.NextRetryAt is not null),
            ReceivedPendingRetry = storage.ReceivedMessages.Values.Count(x => x.NextRetryAt is not null),
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

            if (!string.IsNullOrEmpty(query.StatusName))
            {
                expression = expression.Where(x =>
                    x.StatusName.ToString().Equals(query.StatusName, StringComparison.OrdinalIgnoreCase)
                );
            }

            if (!string.IsNullOrEmpty(query.Name))
            {
                expression = expression.Where(x => x.Name.Equals(query.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(query.Content))
            {
                expression = expression.Where(x => x.Content.Contains(query.Content, StringComparison.Ordinal));
            }

            var offset = query.CurrentPage * query.PageSize;
            var size = query.PageSize;

            var allItems = expression
                .Select(x => new MessageView
                {
                    Added = x.Added,
                    StorageId = x.StorageId,
                    MessageId = x.Origin.GetId(),
                    Version = "N/A",
                    Content = x.Content,
                    ExpiresAt = x.ExpiresAt,
                    Name = x.Name,
                    Retries = x.Retries,
                    StatusName = x.StatusName.ToString(),
                    NextRetryAt = x.NextRetryAt,
                    LockedUntil = x.LockedUntil,
                })
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<MessageView>(
                    allItems.Skip(offset).Take(size).ToList(),
                    query.CurrentPage,
                    query.PageSize,
                    allItems.Count
                )
            );
        }
        else
        {
            var expression = storage.ReceivedMessages.Values.Where(x => true);

            if (!string.IsNullOrEmpty(query.StatusName))
            {
                expression = expression.Where(x =>
                    x.StatusName.ToString().Equals(query.StatusName, StringComparison.OrdinalIgnoreCase)
                );
            }

            if (!string.IsNullOrEmpty(query.Name))
            {
                expression = expression.Where(x => x.Name.Equals(query.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(query.Group))
            {
                expression = expression.Where(x =>
                    x.Group is not null && x.Group.Equals(query.Group, StringComparison.OrdinalIgnoreCase)
                );
            }

            if (!string.IsNullOrEmpty(query.Content))
            {
                expression = expression.Where(x => x.Content.Contains(query.Content, StringComparison.Ordinal));
            }

            var offset = query.CurrentPage * query.PageSize;
            var size = query.PageSize;

            var allItems = expression
                .Select(x => new MessageView
                {
                    Added = x.Added,
                    Group = x.Group,
                    StorageId = x.StorageId,
                    MessageId = x.Origin.GetId(),
                    Version = "N/A",
                    Content = x.Content,
                    ExpiresAt = x.ExpiresAt,
                    Name = x.Name,
                    Retries = x.Retries,
                    StatusName = x.StatusName.ToString(),
                    NextRetryAt = x.NextRetryAt,
                    LockedUntil = x.LockedUntil,
                })
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<MessageView>(
                    allItems.Skip(offset).Take(size).ToList(),
                    query.CurrentPage,
                    query.PageSize,
                    allItems.Count
                )
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

        Dictionary<string, int> valuesMap;

        if (type == MessageType.Publish)
        {
            valuesMap = storage
                .PublishedMessages.Values.Where(x =>
                    string.Equals(x.StatusName.ToString(), statusName, StringComparison.Ordinal)
                )
                .GroupBy(x => x.Added.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        }
        else
        {
            valuesMap = storage
                .ReceivedMessages.Values.Where(x =>
                    string.Equals(x.StatusName.ToString(), statusName, StringComparison.Ordinal)
                )
                .GroupBy(x => x.Added.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        }

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
