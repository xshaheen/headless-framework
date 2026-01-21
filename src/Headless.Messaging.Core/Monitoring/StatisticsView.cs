// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

public class StatisticsView
{
    public int Servers { get; set; }

    public int Subscribers { get; set; }

    public int PublishedSucceeded { get; set; }

    public int PublishedDelayed { get; set; }

    public int ReceivedSucceeded { get; set; }

    public int PublishedFailed { get; set; }
    public int ReceivedFailed { get; set; }
}
