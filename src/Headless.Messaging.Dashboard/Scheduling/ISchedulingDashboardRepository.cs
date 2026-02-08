// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Scheduling;

namespace Headless.Messaging.Dashboard.Scheduling;

/// <summary>
/// Repository interface for scheduling dashboard queries.
/// </summary>
public interface ISchedulingDashboardRepository
{
    /// <summary>Gets all scheduled jobs with optional filtering.</summary>
    Task<IReadOnlyList<ScheduledJob>> GetJobsAsync(
        string? nameFilter = null,
        string? statusFilter = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets a scheduled job by name.</summary>
    Task<ScheduledJob?> GetJobByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Gets paginated job executions for a specific job.</summary>
    Task<IReadOnlyList<JobExecution>> GetExecutionsAsync(
        Guid jobId,
        int page = 0,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets status counts grouped by date for graph data.</summary>
    Task<IReadOnlyList<StatusCountByDate>> GetExecutionGraphDataAsync(
        Guid jobId,
        int days = 7,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets scheduler status summary (total jobs, running, pending, failed by machine).</summary>
    Task<SchedulerStatusSummary> GetSchedulerStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Status count for a given date and status.</summary>
public sealed class StatusCountByDate
{
    public required DateTimeOffset Date { get; init; }
    public required string Status { get; init; }
    public required int Count { get; init; }
}

/// <summary>Scheduler status summary for dashboard.</summary>
public sealed class SchedulerStatusSummary
{
    public required int TotalJobs { get; init; }
    public required int RunningJobs { get; init; }
    public required int PendingJobs { get; init; }
    public required int DisabledJobs { get; init; }
    public required IReadOnlyList<MachineJobCount> JobsByMachine { get; init; }
}

/// <summary>Job count per machine (using LockHolder field).</summary>
public sealed class MachineJobCount
{
    public required string Machine { get; init; }
    public required int RunningCount { get; init; }
}
