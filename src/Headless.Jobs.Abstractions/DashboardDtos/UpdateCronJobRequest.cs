// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.DashboardDtos;

public class UpdateCronJobRequest
{
    public string Function { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? Request { get; set; }
    public int? Retries { get; set; }
    public string? Description { get; set; }
    public int[]? Intervals { get; set; }
}
