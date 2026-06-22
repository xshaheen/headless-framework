// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

/// <summary>
/// Aggregate statistics snapshot returned by <see cref="IMonitoringApi.GetStatisticsAsync"/>.
/// All counts reflect the live state of the storage tables at the time of the query.
/// </summary>
[PublicAPI]
public class StatisticsView
{
    /// <summary>Gets or sets the number of active messaging server instances observed in storage.</summary>
    public int Servers { get; set; }

    /// <summary>Gets or sets the total number of registered consumer subscriptions across all groups.</summary>
    public int Subscribers { get; set; }

    /// <summary>Gets or sets the total number of published messages that reached the <c>Succeeded</c> terminal state.</summary>
    public long PublishedSucceeded { get; set; }

    /// <summary>Gets or sets the number of published messages currently in a <c>Delayed</c> (scheduled) state awaiting dispatch.</summary>
    public long PublishedDelayed { get; set; }

    /// <summary>Gets or sets the total number of received messages that reached the <c>Succeeded</c> terminal state.</summary>
    public long ReceivedSucceeded { get; set; }

    /// <summary>Gets or sets the total number of published messages that reached the <c>Failed</c> terminal state after exhausting retries.</summary>
    public long PublishedFailed { get; set; }

    /// <summary>Gets or sets the total number of received messages that reached the <c>Failed</c> terminal state after exhausting retries.</summary>
    public long ReceivedFailed { get; set; }

    /// <summary>
    /// Count of published rows currently scheduled for a future retry pickup
    /// (<c>NextRetryAt IS NOT NULL</c>). Surfaces backlog pressure on the publish retry processor.
    /// </summary>
    public long PublishedPendingRetry { get; set; }

    /// <summary>
    /// Count of received rows currently scheduled for a future retry pickup
    /// (<c>NextRetryAt IS NOT NULL</c>). Surfaces backlog pressure on the consume retry processor.
    /// </summary>
    public long ReceivedPendingRetry { get; set; }
}
