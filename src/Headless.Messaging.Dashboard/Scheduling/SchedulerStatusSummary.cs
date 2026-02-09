// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.Scheduling;

/// <summary>Scheduler status summary for dashboard.</summary>
internal sealed class SchedulerStatusSummary
{
    public required int TotalJobs { get; init; }
    public required int RunningJobs { get; init; }
    public required int PendingJobs { get; init; }
    public required int DisabledJobs { get; init; }
    public required IReadOnlyList<MachineJobCount> JobsByMachine { get; init; }
}

/// <summary>Job count per machine (using LockHolder field).</summary>
internal sealed class MachineJobCount
{
    public required string Machine { get; init; }
    public required int RunningCount { get; init; }
}
