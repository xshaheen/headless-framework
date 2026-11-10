// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging sidebar's counts: one capped count per list entry, keyed by the <c>countKey</c> its sidebar entry
/// declares, the same count the list shows when filtered the same way.
/// </summary>
/// <remarks>
/// <para>The count keys, which the module's front end reads:</para>
/// <list type="table">
///   <listheader><term>Key</term><description>Rows counted</description></listheader>
///   <item><term><c>published.all</c></term><description>Every published row.</description></item>
///   <item><term><c>published.succeeded</c></term><description>Published rows sent.</description></item>
///   <item><term><c>published.failed</c></term><description>Published rows that failed for good: <c>Failed</c> with no retry pending.</description></item>
///   <item><term><c>published.retrying</c></term><description>Published rows that failed with a retry pending.</description></item>
///   <item><term><c>published.delayed</c></term><description>Published rows waiting for their delivery time.</description></item>
///   <item><term><c>published.scheduled</c></term><description>Published rows stored and waiting for their first send.</description></item>
///   <item><term><c>published.queued</c></term><description>Published rows a sender claimed.</description></item>
///   <item><term><c>received.all</c></term><description>Every received row.</description></item>
///   <item><term><c>received.succeeded</c></term><description>Received rows consumed.</description></item>
///   <item><term><c>received.failed</c></term><description>Received rows that failed for good.</description></item>
///   <item><term><c>received.retrying</c></term><description>Received rows that failed with a retry pending.</description></item>
///   <item><term><c>received.held</c></term><description>Received rows an operator held.</description></item>
///   <item><term><c>hosts.unknown-lanes</c></term><description>Rows of every table whose stored lane this host does not recognize.</description></item>
/// </list>
/// <para>
/// A published count adds up every store. A store that throws or does not answer within the store timeout is named in
/// <see cref="DashboardCount.UnavailableSources"/> of the counts it would have added to, so the shell shows the
/// others' sum as <c>N+</c>; the received counts read the main store alone. A retrying count is the store's failed
/// rows less its terminal ones, both capped, so above the cap it reports the cap less the terminal count, capped.
/// </para>
/// </remarks>
internal sealed class MessagingCounts(MessagingStores stores)
{
    public const string PublishedAll = "published.all";
    public const string PublishedSucceeded = "published.succeeded";
    public const string PublishedFailed = "published.failed";
    public const string PublishedRetrying = "published.retrying";
    public const string PublishedDelayed = "published.delayed";
    public const string PublishedScheduled = "published.scheduled";
    public const string PublishedQueued = "published.queued";
    public const string ReceivedAll = "received.all";
    public const string ReceivedSucceeded = "received.succeeded";
    public const string ReceivedFailed = "received.failed";
    public const string ReceivedRetrying = "received.retrying";
    public const string ReceivedHeld = "received.held";
    public const string UnknownLanes = "hosts.unknown-lanes";

    public async Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(CancellationToken cancellationToken)
    {
        var answers = await stores.ReadEachAsync(stores.All, _CountStoreAsync, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, DashboardCount>(StringComparer.Ordinal);

        foreach (var key in (string[])[.. _PublishedKeys, .. _ReceivedKeys, UnknownLanes])
        {
            var count = 0L;
            var capped = false;
            var unavailable = new List<string>();
            var answered = false;

            foreach (var (store, counts) in answers.Answered)
            {
                if (counts.TryGetValue(key, out var value))
                {
                    count += value.Count;
                    capped |= value.Capped;
                    answered = true;
                }
            }

            foreach (var name in answers.Unavailable)
            {
                // The main store answers every key; an outbox only the published ones and its unknown lanes.
                if (
                    _PublishedKeys.Contains(key, StringComparer.Ordinal)
                    || string.Equals(key, UnknownLanes, StringComparison.Ordinal)
                    || string.Equals(name, stores.Primary.Name, StringComparison.OrdinalIgnoreCase)
                )
                {
                    unavailable.Add(name);
                }
            }

            if (answered || unavailable.Count > 0)
            {
                result[key] = new DashboardCount
                {
                    Count = count,
                    Capped = capped,
                    UnavailableSources = unavailable,
                };
            }
        }

        return result;
    }

    private static readonly string[] _PublishedKeys =
    [
        PublishedAll,
        PublishedSucceeded,
        PublishedFailed,
        PublishedRetrying,
        PublishedDelayed,
        PublishedScheduled,
        PublishedQueued,
    ];

    private static readonly string[] _ReceivedKeys =
    [
        ReceivedAll,
        ReceivedSucceeded,
        ReceivedFailed,
        ReceivedRetrying,
        ReceivedHeld,
    ];

    // One count at a time, so one counts request holds at most one connection per store.
    private static async Task<Dictionary<string, MessageCount>> _CountStoreAsync(
        MessagingStore store,
        CancellationToken cancellationToken
    )
    {
        var monitoring = store.Monitoring;
        var counts = new Dictionary<string, MessageCount>(StringComparer.Ordinal);

        async Task<MessageCount> count(MessageListQuery query) =>
            await monitoring
                .CountMessagesAsync(query, MessageCount.DefaultCap, cancellationToken)
                .ConfigureAwait(false);

        var published = new MessageListQuery { MessageType = MessageType.Publish };
        counts[PublishedAll] = await count(published).ConfigureAwait(false);
        counts[PublishedSucceeded] = await count(published with { Status = StatusName.Succeeded })
            .ConfigureAwait(false);
        var publishedTerminal = await count(published with { Status = StatusName.Failed, TerminalOnly = true })
            .ConfigureAwait(false);
        counts[PublishedFailed] = publishedTerminal;
        counts[PublishedRetrying] = _Retrying(
            await count(published with { Status = StatusName.Failed }).ConfigureAwait(false),
            publishedTerminal
        );
        counts[PublishedDelayed] = await count(published with { Status = StatusName.Delayed }).ConfigureAwait(false);
        counts[PublishedScheduled] = await count(published with { Status = StatusName.Scheduled })
            .ConfigureAwait(false);
        counts[PublishedQueued] = await count(published with { Status = StatusName.Queued }).ConfigureAwait(false);

        var unknown = await _UnknownLanesAsync(monitoring, MessageType.Publish, cancellationToken)
            .ConfigureAwait(false);

        if (store.HoldsReceived)
        {
            var received = new MessageListQuery { MessageType = MessageType.Subscribe };
            counts[ReceivedAll] = await count(received).ConfigureAwait(false);
            counts[ReceivedSucceeded] = await count(received with { Status = StatusName.Succeeded })
                .ConfigureAwait(false);
            var receivedTerminal = await count(received with { Status = StatusName.Failed, TerminalOnly = true })
                .ConfigureAwait(false);
            counts[ReceivedFailed] = receivedTerminal;
            counts[ReceivedRetrying] = _Retrying(
                await count(received with { Status = StatusName.Failed }).ConfigureAwait(false),
                receivedTerminal
            );
            counts[ReceivedHeld] = await count(received with { IsHeld = true }).ConfigureAwait(false);
            unknown += await _UnknownLanesAsync(monitoring, MessageType.Subscribe, cancellationToken)
                .ConfigureAwait(false);
        }

        counts[UnknownLanes] = MessageCount.Of(unknown, int.MaxValue);

        return counts;
    }

    private static MessageCount _Retrying(MessageCount failed, MessageCount terminal) =>
        new(Math.Max(0, failed.Count - terminal.Count), failed.Capped);

    private static async Task<int> _UnknownLanesAsync(
        IMonitoringApi monitoring,
        MessageType type,
        CancellationToken cancellationToken
    )
    {
        var page = await monitoring
            .GetUnknownLaneMessagesAsync(
                new UnknownLaneMessageQuery { MessageType = type, PageSize = 1 },
                cancellationToken
            )
            .ConfigureAwait(false);

        return page.TotalItems;
    }
}
