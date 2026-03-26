// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

public class StatisticsView
{
    public int Servers { get; set; }

    public int Subscribers { get; set; }

    public long PublishedSucceeded { get; set; }

    public long PublishedDelayed { get; set; }

    public long ReceivedSucceeded { get; set; }

    public long PublishedFailed { get; set; }
    public long ReceivedFailed { get; set; }
}
