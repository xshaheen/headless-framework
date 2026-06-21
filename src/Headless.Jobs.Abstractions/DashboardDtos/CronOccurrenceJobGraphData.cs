// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.DashboardDtos;

public class CronOccurrenceJobGraphData
{
    public DateTime Date { get; set; }
    public Tuple<int, int>[]? Results { get; set; }
}
