// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Primitives;

namespace Headless.Messaging.Storage.InMemory;

internal sealed class InMemoryMonitoringApi(InMemoryDataStorage storage, TimeProvider timeProvider) : IMonitoringApi
{
    public ValueTask<MediumMessage?> GetPublishedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            storage.PublishedMessages.TryGetValue(id, out var message) && _IsRecognizedLane(message.Lane)
                ? (MediumMessage?)message
                : null
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
            if (storage.PublishedMessages.TryGetValue(id, out var message) && _IsRecognizedLane(message.Lane))
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
            storage.ReceivedMessages.TryGetValue(id, out var message) && _IsRecognizedLane(message.Lane)
                ? (MediumMessage?)message
                : null
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
            if (storage.ReceivedMessages.TryGetValue(id, out var message) && _IsRecognizedLane(message.Lane))
            {
                result.Add(message);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<MediumMessage>>(result);
    }

    public ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var publishedSucceeded = 0;
        var publishedFailed = 0;
        var publishedDelayed = 0;
        var publishedPendingRetry = 0;

        // Single pass over each value collection — the original implementation enumerated the
        // dictionary up to 4× per side; this collapses it to 1×.
        foreach (var msg in storage.PublishedMessages.Values)
        {
            if (!_IsRecognizedLane(msg.Lane))
            {
                continue;
            }

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

        var receivedSucceeded = 0;
        var receivedFailed = 0;
        var receivedPendingRetry = 0;

        foreach (var msg in storage.ReceivedMessages.Values)
        {
            if (!_IsRecognizedLane(msg.Lane))
            {
                continue;
            }

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

    public ValueTask<BacklogView> GetBacklogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        var (publishedPending, oldestPublished) = _Backlog(storage.PublishedMessages.Values, now);
        var (receivedPending, oldestReceived) = _Backlog(storage.ReceivedMessages.Values, now);

        return ValueTask.FromResult(
            new BacklogView
            {
                PublishedPending = publishedPending,
                ReceivedPending = receivedPending,
                OldestPublishedPendingAge = _AgeSince(oldestPublished, now),
                OldestReceivedPendingAge = _AgeSince(oldestReceived, now),
            }
        );
    }

    private static (long Pending, DateTimeOffset? Oldest) _Backlog(
        IEnumerable<MemoryMessage> messages,
        DateTimeOffset now
    )
    {
        long pending = 0;
        DateTimeOffset? oldest = null;

        foreach (var message in messages)
        {
            if (!_IsRecognizedLane(message.Lane))
            {
                continue;
            }

            var since = MessageBacklogState.PendingSince(
                message.StatusName,
                message.Added,
                message.ExpiresAt,
                message.NextRetryAt,
                now
            );
            if (since is not { } pendingSince)
            {
                continue;
            }

            pending++;
            if (oldest is null || pendingSince < oldest)
            {
                oldest = pendingSince;
            }
        }

        return (pending, oldest);
    }

    private static TimeSpan? _AgeSince(DateTimeOffset? since, DateTimeOffset now)
    {
        return since is { } value ? TimeSpan.FromTicks(Math.Max(0, (now - value).Ticks)) : null;
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

        // CurrentPage is zero-based (it is returned as IndexPage.Index). Clamping keeps a negative
        // index from throwing out of Skip.
        var currentPage = Math.Max(query.CurrentPage, 0);
        var offset = (int)Math.Min((long)currentPage * query.PageSize, int.MaxValue);

        if (query.MessageType == MessageType.Publish)
        {
            var expression = storage.PublishedMessages.Values.Where(x => _IsRecognizedLane(x.Lane));

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

            if (query.Lane is { } lane)
            {
                expression = expression.Where(x => x.Lane == lane);
            }

            var size = query.PageSize;

            // Materialize the filtered list once, then skip/take to project only the requested
            // page — avoids allocating MessageView instances for the rows we discard.
            var filtered = expression.ToList();
            var pageItems = filtered
                .Skip(offset)
                .Take(size)
                .Select(x =>
                {
                    var delivery = DeliveryMetadata.ReadStoredHeaders(x.Origin.Headers);
                    return new MessageView
                    {
                        Added = x.Added,
                        StorageId = x.StorageId,
                        MessageId = x.Origin.Id,
                        Version = "N/A",
                        Content = x.Content,
                        Lane = x.Lane,
                        RequestedDeliveryMode = delivery.RequestedDeliveryMode,
                        ResolvedDeliveryMode = delivery.ResolvedDeliveryMode,
                        IsCoordinated = delivery.IsCoordinated,
                        ExpiresAt = x.ExpiresAt,
                        Name = x.Name,
                        Retries = x.Retries,
                        StatusName = x.StatusName,
                        NextRetryAt = x.NextRetryAt,
                        LockedUntil = x.LockedUntil,
                    };
                })
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<MessageView>(pageItems, currentPage, query.PageSize, filtered.Count)
            );
        }

        {
            var expression = storage.ReceivedMessages.Values.Where(x => _IsRecognizedLane(x.Lane));

            if (query.StatusName is not null)
            {
                expression = expression.Where(x => x.StatusName == query.StatusName);
            }

            if (!string.IsNullOrEmpty(query.Name))
            {
                expression = expression.Where(x => x.Name.Equals(query.Name, StringComparison.OrdinalIgnoreCase));
            }

            // Identities compare ordinally, matching the binary collation the SQL providers store them under.
            if (!string.IsNullOrEmpty(query.ConsumerIdentity))
            {
                expression = expression.Where(x =>
                    string.Equals(x.ConsumerIdentity, query.ConsumerIdentity, StringComparison.Ordinal)
                );
            }

            if (!string.IsNullOrEmpty(query.Content))
            {
                expression = expression.Where(x => x.Content.Contains(query.Content, StringComparison.Ordinal));
            }

            if (query.Lane is { } lane)
            {
                expression = expression.Where(x => x.Lane == lane);
            }

            var size = query.PageSize;

            var filtered = expression.ToList();
            var pageItems = filtered
                .Skip(offset)
                .Take(size)
                .Select(x =>
                {
                    var delivery = DeliveryMetadata.ReadStoredHeaders(x.Origin.Headers);
                    return new MessageView
                    {
                        Added = x.Added,
                        ConsumerIdentity = x.ConsumerIdentity,
                        StorageId = x.StorageId,
                        MessageId = x.Origin.Id,
                        Version = "N/A",
                        Content = x.Content,
                        Lane = x.Lane,
                        RequestedDeliveryMode = delivery.RequestedDeliveryMode,
                        ResolvedDeliveryMode = delivery.ResolvedDeliveryMode,
                        IsCoordinated = delivery.IsCoordinated,
                        ExpiresAt = x.ExpiresAt,
                        Name = x.Name,
                        Retries = x.Retries,
                        StatusName = x.StatusName,
                        NextRetryAt = x.NextRetryAt,
                        LockedUntil = x.LockedUntil,
                    };
                })
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<MessageView>(pageItems, currentPage, query.PageSize, filtered.Count)
            );
        }
    }

    public ValueTask<MessageListPage> ListMessagesAsync(
        MessageListQuery query,
        MessageListPosition? after,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsInclusiveBetween(pageSize, 1, 500);
        cancellationToken.ThrowIfCancellationRequested();

        var received = query.MessageType == MessageType.Subscribe;
        var rows = _Rows(query)
            .Where(message =>
                after is not { } position
                || message.Added < position.Added
                || (message.Added == position.Added && message.StorageId.CompareTo(position.Id) < 0)
            )
            .OrderByDescending(static message => message.Added)
            .ThenByDescending(static message => message.StorageId)
            .Take(pageSize + 1)
            .Select(message => new MessageView
            {
                StorageId = message.StorageId,
                MessageId = message.Origin.Id,
                Version = message.Version,
                Name = message.Name,
                ConsumerIdentity = received ? message.ConsumerIdentity : null,
                Lane = message.Lane,
                Retries = message.Retries,
                Added = message.Added,
                ExpiresAt = message.ExpiresAt,
                StatusName = message.StatusName,
                NextRetryAt = message.NextRetryAt,
                LockedUntil = message.LockedUntil,
                ChangedAt = message.ChangedAt,
                FailureFingerprint = message.FailureFingerprint,
                TenantId = received ? message.InboxKey?.TenantId : null,
                IsHeld = received && message.IsHeld,
                IncarnationId = message.InboxGeneration?.IncarnationId,
            })
            .ToList();

        var page = rows.Count > pageSize ? rows.GetRange(0, pageSize) : rows;
        return ValueTask.FromResult(
            new MessageListPage
            {
                Items = page,
                Next = rows.Count > pageSize ? new MessageListPosition(page[^1].Added, page[^1].StorageId) : null,
            }
        );
    }

    public ValueTask<MessageCount> CountMessagesAsync(
        MessageListQuery query,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(cap);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(MessageCount.Of(_Rows(query).Take(cap + 1).Count(), cap));
    }

    public ValueTask<IReadOnlyList<MessageFailureGroup>> GetFailureGroupsAsync(
        MessageType messageType,
        string? consumerIdentity = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var received = messageType == MessageType.Subscribe;
        var source = received ? storage.ReceivedMessages.Values : storage.PublishedMessages.Values;
        var groups = source
            .Where(message =>
                InMemoryMessageFilter.IsRecognizedLane(message.Lane)
                && message.StatusName == StatusName.Failed
                && message.NextRetryAt is null
                && (!received || message.IsCurrentGeneration)
                && (
                    !received
                    || consumerIdentity is null
                    || string.Equals(message.ConsumerIdentity, consumerIdentity, StringComparison.Ordinal)
                )
            )
            .GroupBy(
                message =>
                    (Fingerprint: message.FailureFingerprint, Consumer: received ? message.ConsumerIdentity : null),
                FailureGroupKeyComparer.Instance
            )
            .Select(group => new MessageFailureGroup
            {
                MessageType = messageType,
                FailureFingerprint = group.Key.Fingerprint,
                ConsumerIdentity = group.Key.Consumer,
                Count = group.Count(),
                LastFailedAt = group.Max(static message => message.ChangedAt),
            });

        return ValueTask.FromResult(MessageFailureGroupOrder.Sort(groups));
    }

    private IEnumerable<MemoryMessage> _Rows(MessageListQuery query)
    {
        var source =
            query.MessageType == MessageType.Subscribe
                ? storage.ReceivedMessages.Values
                : storage.PublishedMessages.Values;

        return source.Where(message => InMemoryMessageFilter.Matches(message, query));
    }

    // Groups compare their key text ordinally, as the relational stores compare the identity's hash.
    private sealed class FailureGroupKeyComparer : IEqualityComparer<(string? Fingerprint, string? Consumer)>
    {
        public static readonly FailureGroupKeyComparer Instance = new();

        public bool Equals((string? Fingerprint, string? Consumer) x, (string? Fingerprint, string? Consumer) y) =>
            string.Equals(x.Fingerprint, y.Fingerprint, StringComparison.Ordinal)
            && string.Equals(x.Consumer, y.Consumer, StringComparison.Ordinal);

        public int GetHashCode((string? Fingerprint, string? Consumer) obj) =>
            HashCode.Combine(
                obj.Fingerprint is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Fingerprint),
                obj.Consumer is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Consumer)
            );
    }

    public ValueTask<IndexPage<UnknownLaneMessageView>> GetUnknownLaneMessagesAsync(
        UnknownLaneMessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentPage = Math.Max(query.CurrentPage, 1);
        var pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200);
        var source = query.MessageType switch
        {
            MessageType.Publish => storage.PublishedMessages.Values,
            MessageType.Subscribe => storage.ReceivedMessages.Values,
            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                query.MessageType,
                "Unknown-lane diagnostics require Publish or Subscribe."
            ),
        };
        var unknown = source
            .Where(message => message.Lane is not (MessageLane.Bus or MessageLane.Queue))
            .OrderBy(message => message.Added)
            .ThenBy(message => message.StorageId)
            .ToList();
        var offset = (long)(currentPage - 1) * pageSize;
        var pageRows = offset >= unknown.Count ? [] : unknown.Skip((int)offset).Take(pageSize);
        var items = pageRows
            .Select(message => new UnknownLaneMessageView
            {
                StorageId = message.StorageId,
                MessageType = query.MessageType,
                RawLane = (short)message.Lane,
                Name = message.Name,
                StatusName = message.StatusName,
                Added = message.Added,
                NextRetryAt = message.NextRetryAt,
                LockedUntil = message.LockedUntil,
            })
            .ToList();

        return ValueTask.FromResult(
            new IndexPage<UnknownLaneMessageView>(items, currentPage - 1, pageSize, unknown.Count)
        );
    }

    public ValueTask<long> GetPublishedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(
            storage.PublishedMessages.Values.Count(x => _IsRecognizedLane(x.Lane) && x.StatusName == StatusName.Failed)
        );
    }

    public ValueTask<long> GetPublishedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(
            storage.PublishedMessages.Values.Count(x =>
                _IsRecognizedLane(x.Lane) && x.StatusName == StatusName.Succeeded
            )
        );
    }

    public ValueTask<long> GetReceivedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(
            storage.ReceivedMessages.Values.Count(x => _IsRecognizedLane(x.Lane) && x.StatusName == StatusName.Failed)
        );
    }

    public ValueTask<long> GetReceivedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<long>(
            storage.ReceivedMessages.Values.Count(x =>
                _IsRecognizedLane(x.Lane) && x.StatusName == StatusName.Succeeded
            )
        );
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
            if (!_IsRecognizedLane(message.Lane) || message.StatusName != statusName)
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

    private static bool _IsRecognizedLane(MessageLane lane)
    {
        return lane is MessageLane.Bus or MessageLane.Queue;
    }
}
