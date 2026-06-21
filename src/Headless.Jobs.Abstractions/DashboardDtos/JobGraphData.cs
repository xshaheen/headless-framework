// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.DashboardDtos;

public class JobGraphData
{
    public DateTime Date { get; set; }
    public required Tuple<int, int>[] Results { get; set; }
}
