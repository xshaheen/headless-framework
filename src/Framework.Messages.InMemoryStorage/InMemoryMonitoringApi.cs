// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Primitives;

namespace Framework.Messages;

internal class InMemoryMonitoringApi(TimeProvider timeProvider) : IMonitoringApi
{
    public ValueTask<MediumMessage?> GetPublishedMessageAsync(long id)
    {
        var idString = id.ToString(CultureInfo.InvariantCulture);

        return ValueTask.FromResult<MediumMessage?>(
            InMemoryDataStorage.PublishedMessages.Values.FirstOrDefault(x =>
                string.Equals(x.DbId, idString, StringComparison.Ordinal)
            )
        );
    }

    public ValueTask<MediumMessage?> GetReceivedMessageAsync(long id)
    {
        var idString = id.ToString(CultureInfo.InvariantCulture);

        return ValueTask.FromResult<MediumMessage?>(
            InMemoryDataStorage.ReceivedMessages.Values.FirstOrDefault(x =>
                string.Equals(x.DbId, idString, StringComparison.Ordinal)
            )
        );
    }

    public ValueTask<StatisticsView> GetStatisticsAsync()
    {
        var stats = new StatisticsView
        {
            PublishedSucceeded = InMemoryDataStorage.PublishedMessages.Values.Count(x =>
                x.StatusName == StatusName.Succeeded
            ),
            ReceivedSucceeded = InMemoryDataStorage.ReceivedMessages.Values.Count(x =>
                x.StatusName == StatusName.Succeeded
            ),
            PublishedFailed = InMemoryDataStorage.PublishedMessages.Values.Count(x =>
                x.StatusName == StatusName.Failed
            ),
            ReceivedFailed = InMemoryDataStorage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Failed),
            PublishedDelayed = InMemoryDataStorage.PublishedMessages.Values.Count(x =>
                x.StatusName == StatusName.Delayed
            ),
        };

        return ValueTask.FromResult(stats);
    }

    public ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(MessageType type)
    {
        return _GetHourlyTimelineStats(type, nameof(StatusName.Failed));
    }

    public ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(MessageType type)
    {
        return _GetHourlyTimelineStats(type, nameof(StatusName.Succeeded));
    }

    public ValueTask<IndexPage<MessageView>> GetMessagesAsync(MessageQuery query)
    {
        if (query.MessageType == MessageType.Publish)
        {
            var expression = InMemoryDataStorage.PublishedMessages.Values.Where(x => true);

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
                expression = expression.Where(x => x.Content.Contains(query.Content));
            }

            var offset = query.CurrentPage * query.PageSize;
            var size = query.PageSize;

            var allItems = expression
                .Select(x => new MessageView
                {
                    Added = x.Added,
                    Version = "N/A",
                    Content = x.Content,
                    ExpiresAt = x.ExpiresAt,
                    Id = x.DbId,
                    Name = x.Name,
                    Retries = x.Retries,
                    StatusName = x.StatusName.ToString(),
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
            var expression = InMemoryDataStorage.ReceivedMessages.Values.Where(x => true);

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
                    Version = "N/A",
                    Content = x.Content,
                    ExpiresAt = x.ExpiresAt,
                    Id = x.DbId,
                    Name = x.Name,
                    Retries = x.Retries,
                    StatusName = x.StatusName.ToString(),
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

    public ValueTask<int> PublishedFailedCount()
    {
        return new ValueTask<int>(
            InMemoryDataStorage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Failed)
        );
    }

    public ValueTask<int> PublishedSucceededCount()
    {
        return new ValueTask<int>(
            InMemoryDataStorage.PublishedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded)
        );
    }

    public ValueTask<int> ReceivedFailedCount()
    {
        return new ValueTask<int>(
            InMemoryDataStorage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Failed)
        );
    }

    public ValueTask<int> ReceivedSucceededCount()
    {
        return new ValueTask<int>(
            InMemoryDataStorage.ReceivedMessages.Values.Count(x => x.StatusName == StatusName.Succeeded)
        );
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
            valuesMap = InMemoryDataStorage
                .PublishedMessages.Values.Where(x =>
                    string.Equals(x.StatusName.ToString(), statusName, StringComparison.Ordinal)
                )
                .GroupBy(x => x.Added.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        }
        else
        {
            valuesMap = InMemoryDataStorage
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
