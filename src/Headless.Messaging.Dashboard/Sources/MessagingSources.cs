// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

/// <summary>The Messaging module's read capabilities over every store: Needs you, Overview, id lookup, and payloads.</summary>
internal sealed class MessagingSources(
    MessagingStores stores,
    MessagingCounts counts,
    MessagingMetricsEventListener metrics
)
{
    /// <summary>The most rows one message id lookup lists per store and table.</summary>
    internal const int LookupPageSize = 20;

    private readonly MessagingStores _stores = Argument.IsNotNull(stores);
    private readonly MessagingCounts _counts = Argument.IsNotNull(counts);
    private readonly MessagingMetricsEventListener _metrics = Argument.IsNotNull(metrics);

    /// <summary>
    /// Lists one item per failure fingerprint (and, for received rows, consumer) of the failures that ended for good,
    /// in every store, and one per store and table that holds rows in lanes this host does not recognize.
    /// </summary>
    public async Task<DashboardAttentionAnswer> GetAttentionAsync(CancellationToken cancellationToken)
    {
        var answers = await _stores
            .ReadEachAsync(_stores.All, _ReadAttentionAsync, cancellationToken)
            .ConfigureAwait(false);

        return new DashboardAttentionAnswer
        {
            Items = [.. answers.Answered.SelectMany(static answer => answer.Value)],
            UnavailableSources = answers.Unavailable,
        };
    }

    /// <summary>
    /// The messages that failed for good and the ones waiting for a retry, over every store, and this node's consume
    /// rate over the last minute, left out while the node measured no second of it.
    /// </summary>
    public async Task<DashboardOverviewAnswer> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var sidebar = await _counts.GetCountsAsync(cancellationToken).ConfigureAwait(false);
        List<DashboardFigure> figures =
        [
            new DashboardFigure
            {
                Id = MessagingDashboardOptions.ModuleId + ".overview.failed",
                Label = "Messages that failed for good",
                Value = _Sum(sidebar, MessagingCounts.PublishedFailed, MessagingCounts.ReceivedFailed),
            },
            new DashboardFigure
            {
                Id = MessagingDashboardOptions.ModuleId + ".overview.retrying",
                Label = "Messages waiting for a retry",
                Value = _Sum(sidebar, MessagingCounts.PublishedRetrying, MessagingCounts.ReceivedRetrying),
            },
        ];

        if (_ConsumeRate() is { } rate)
        {
            figures.Add(
                new DashboardFigure
                {
                    Id = MessagingDashboardOptions.ModuleId + ".overview.consume-rate",
                    Label = "Consumed on this node, last minute",
                    Value = Math.Round(rate, 1, MidpointRounding.AwayFromZero),
                    Unit = "/s",
                }
            );
        }

        return new DashboardOverviewAnswer
        {
            Figures = figures,
            Tracks = [],
            UnavailableSources =
            [
                .. sidebar.Values.SelectMany(static count => count.UnavailableSources).Distinct(StringComparer.Ordinal),
            ],
        };
    }

    /// <summary>
    /// Returns the rows <paramref name="query"/> names: a full object id, a bare storage id, or a message id, which is
    /// looked up in every store and listed with each match's store.
    /// </summary>
    public async Task<IReadOnlyList<DashboardObject>> ResolveAsync(string query, CancellationToken cancellationToken)
    {
        if (MessagingObjectId.TryParse(query, out var objectId))
        {
            // An id another node served is that node's to answer; this node serves only its own.
            if (
                !string.Equals(objectId.Node, _stores.NodeName, StringComparison.Ordinal)
                || _stores.Find(objectId.Store) is not { } named
            )
            {
                return [];
            }

            return await _FindByStorageIdAsync([named], objectId.StorageId, cancellationToken).ConfigureAwait(false);
        }

        var found = new List<DashboardObject>();

        if (Guid.TryParse(query, out var storageId))
        {
            found.AddRange(
                await _FindByStorageIdAsync(_stores.All, storageId, cancellationToken).ConfigureAwait(false)
            );
        }

        // A message id may itself look like a storage id, so a Guid is looked up both ways.
        found.AddRange(await _FindByMessageIdAsync(query, cancellationToken).ConfigureAwait(false));

        return [.. found.DistinctBy(static found => (found.Target.Kind, found.Target.Id))];
    }

    /// <summary>Returns a row's stored envelope as JSON, or <see langword="null"/> when the row is not found.</summary>
    public async Task<DashboardPayload?> GetPayloadAsync(ObjectRef target, CancellationToken cancellationToken)
    {
        if (await FindAsync(target, cancellationToken).ConfigureAwait(false) is not { } found)
        {
            return null;
        }

        return new DashboardPayload { Content = found.Message.Content, ContentType = "application/json" };
    }

    /// <summary>
    /// Reads the row an object reference of this module names on this node, or <see langword="null"/> when the
    /// reference is not one, names another node or an unknown store, or the row is gone.
    /// </summary>
    public async Task<(MessageType Type, MessagingObjectId Id, MessagingStore Store, MediumMessage Message)?> FindAsync(
        ObjectRef target,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(target);

        if (
            !string.Equals(target.Module, MessagingDashboardOptions.ModuleId, StringComparison.Ordinal)
            || !MessagingViews.TryParseKind(target.Kind, out var type)
            || !MessagingObjectId.TryParse(target.Id, out var id)
        )
        {
            return null;
        }

        return await FindAsync(type, id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="FindAsync(ObjectRef, CancellationToken)"/>
    public async Task<(MessageType Type, MessagingObjectId Id, MessagingStore Store, MediumMessage Message)?> FindAsync(
        MessageType type,
        MessagingObjectId id,
        CancellationToken cancellationToken
    )
    {
        if (
            !string.Equals(id.Node, _stores.NodeName, StringComparison.Ordinal)
            || _stores.Find(id.Store) is not { } store
            || (type == MessageType.Subscribe && !store.HoldsReceived)
        )
        {
            return null;
        }

        var message =
            type == MessageType.Publish
                ? await store.Monitoring.GetPublishedMessageAsync(id.StorageId, cancellationToken).ConfigureAwait(false)
                : await store.Monitoring.GetReceivedMessageAsync(id.StorageId, cancellationToken).ConfigureAwait(false);

        // The id names the store by its canonical name, whatever case the caller wrote.
        return message is null ? null : (type, id with { Store = store.Name }, store, message);
    }

    private async Task<IReadOnlyList<DashboardAttentionItem>> _ReadAttentionAsync(
        MessagingStore store,
        CancellationToken cancellationToken
    )
    {
        var items = new List<DashboardAttentionItem>();
        var monitoring = store.Monitoring;
        MessageType[] types = store.HoldsReceived
            ? [MessageType.Publish, MessageType.Subscribe]
            : [MessageType.Publish];

        foreach (var type in types)
        {
            var groups = await monitoring
                .GetFailureGroupsAsync(type, consumerIdentity: null, cancellationToken)
                .ConfigureAwait(false);

            items.AddRange(groups.Select(group => _FailureItem(store, type, group)));

            var unknown = await monitoring
                .GetUnknownLaneMessagesAsync(
                    new UnknownLaneMessageQuery { MessageType = type, PageSize = 1 },
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (unknown.TotalItems > 0)
            {
                var kind = MessagingViews.KindOf(type);
                var key = $"unknown-lane:{store.Name}:{kind}";

                items.Add(
                    new DashboardAttentionItem
                    {
                        Id = key,
                        GroupKey = key,
                        Title =
                            unknown.TotalItems == 1
                                ? $"1 {kind} row in {store.Name} is in a lane this host does not recognize"
                                : $"{unknown.TotalItems.ToString(CultureInfo.InvariantCulture)} {kind} rows in {store.Name} are in lanes this host does not recognize",
                        Severity = DashboardAttentionSeverity.Warning,
                        Count = unknown.TotalItems,
                    }
                );
            }
        }

        return items;
    }

    private static DashboardAttentionItem _FailureItem(
        MessagingStore store,
        MessageType type,
        MessageFailureGroup group
    )
    {
        var kind = MessagingViews.KindOf(type);
        var fingerprint = group.FailureFingerprint ?? string.Empty;
        var consumer = group.ConsumerIdentity;
        // One cause is one fingerprint of one consumer, whichever store holds its rows.
        var key = $"failed:{kind}:{fingerprint}:{consumer}";
        var count = group.Count.ToString(CultureInfo.InvariantCulture);
        var what = type == MessageType.Subscribe ? $"received by {consumer}" : $"published to {store.Name}";

        return new DashboardAttentionItem
        {
            Id = $"{key}:{store.Name}",
            GroupKey = key,
            Title = group.Count == 1 ? $"1 message {what} failed for good" : $"{count} messages {what} failed for good",
            Severity = DashboardAttentionSeverity.Critical,
            Count = group.Count,
            Target = consumer is null ? null : MessagingViews.Ref(MessagingViews.ConsumerKind, consumer),
            LastSeenAt = group.LastFailedAt,
            // A list filter cannot select rows without a fingerprint, so such a group carries no filter that could
            // reach past it.
            GroupFilter = group.FailureFingerprint is null
                ? null
                : JsonSerializer.SerializeToElement(
                    new MessagingListScope
                    {
                        Store = store.Name,
                        Status = StatusName.Failed,
                        TerminalOnly = true,
                        Consumer = consumer,
                        Fingerprint = group.FailureFingerprint,
                    },
                    MessagingDashboardJsonContext.Default.MessagingListScope
                ),
        };
    }

    private async Task<IReadOnlyList<DashboardObject>> _FindByStorageIdAsync(
        IReadOnlyList<MessagingStore> targets,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        var answers = await _stores
            .ReadEachAsync(
                targets,
                async (store, token) =>
                {
                    var found = new List<DashboardObject>(2);
                    var id = new MessagingObjectId(_stores.NodeName, store.Name, storageId);

                    if (
                        await store.Monitoring.GetPublishedMessageAsync(storageId, token).ConfigureAwait(false) is
                        { } published
                    )
                    {
                        found.Add(_Object(MessageType.Publish, id, store, published));
                    }

                    if (
                        store.HoldsReceived
                        && await store.Monitoring.GetReceivedMessageAsync(storageId, token).ConfigureAwait(false)
                            is { } received
                    )
                    {
                        found.Add(_Object(MessageType.Subscribe, id, store, received));
                    }

                    return found;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return [.. answers.Answered.SelectMany(static answer => answer.Value)];
    }

    private async Task<IReadOnlyList<DashboardObject>> _FindByMessageIdAsync(
        string messageId,
        CancellationToken cancellationToken
    )
    {
        var answers = await _stores
            .ReadEachAsync(
                _stores.All,
                async (store, token) =>
                {
                    var found = new List<DashboardObject>();
                    MessageType[] types = store.HoldsReceived
                        ? [MessageType.Publish, MessageType.Subscribe]
                        : [MessageType.Publish];

                    foreach (var type in types)
                    {
                        var page = await store
                            .Monitoring.ListMessagesAsync(
                                new MessageListQuery { MessageType = type, MessageId = messageId },
                                after: null,
                                LookupPageSize,
                                token
                            )
                            .ConfigureAwait(false);

                        // SQL Server's '=' ignores trailing spaces, so the store's match is confirmed byte for byte.
                        found.AddRange(
                            page.Items.Where(row => string.Equals(row.MessageId, messageId, StringComparison.Ordinal))
                                .Select(row => new DashboardObject
                                {
                                    Target = MessagingViews.Ref(
                                        type,
                                        new MessagingObjectId(_stores.NodeName, store.Name, row.StorageId)
                                    ),
                                    Title = _Title(row.Name, store),
                                    Status = row.StatusName.ToString(),
                                })
                        );
                    }

                    return found;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return [.. answers.Answered.SelectMany(static answer => answer.Value)];
    }

    private static DashboardObject _Object(
        MessageType type,
        MessagingObjectId id,
        MessagingStore store,
        MediumMessage message
    ) =>
        MessagingViews.ToObject(type, id, message) with
        {
            Title = _Title(MessagingViews.MessageNameOf(message), store),
        };

    // Every match names its store, because one message id can be in the main store and an outbox at once.
    private static string _Title(string? name, MessagingStore store) => $"{name ?? "(unnamed)"} · {store.Name}";

    private static double _Sum(IReadOnlyDictionary<string, DashboardCount> counts, params ReadOnlySpan<string> keys)
    {
        var sum = 0L;

        foreach (var key in keys)
        {
            sum += counts.TryGetValue(key, out var count) ? count.Count : 0;
        }

        return sum;
    }

    private double? _ConsumeRate()
    {
        var measured = _metrics.LastSeconds(60).OfType<int>().ToArray();

        return measured.Length == 0 ? null : measured.Average();
    }
}
