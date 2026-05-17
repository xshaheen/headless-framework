// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

[PublicAPI]
public class StatisticsView
{
    public int Servers { get; set; }

    public int Subscribers { get; set; }

    public long PublishedSucceeded { get; set; }

    public long PublishedDelayed { get; set; }

    public long ReceivedSucceeded { get; set; }

    public long PublishedFailed { get; set; }

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
