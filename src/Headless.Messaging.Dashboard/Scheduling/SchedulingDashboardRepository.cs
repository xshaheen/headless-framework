// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.Scheduling;

/// <summary>
/// Default scheduling dashboard repository that delegates to <see cref="IScheduledJobStorage"/>.
/// </summary>
internal sealed class SchedulingDashboardRepository(IScheduledJobStorage storage) : ISchedulingDashboardRepository
{
    public async Task<IReadOnlyList<ScheduledJob>> GetJobsAsync(
        string? nameFilter = null,
        string? statusFilter = null,
        CancellationToken cancellationToken = default
    )
    {
        var jobs = await storage.GetAllJobsAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(nameFilter))
        {
            jobs = jobs.Where(j => j.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (
            !string.IsNullOrEmpty(statusFilter)
            && Enum.TryParse<ScheduledJobStatus>(statusFilter, ignoreCase: true, out var status)
        )
        {
            jobs = jobs.Where(j => j.Status == status).ToList();
        }

        return jobs.OrderBy(j => j.Name, StringComparer.Ordinal).ToList();
    }

    public Task<ScheduledJob?> GetJobByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return storage.GetJobByNameAsync(name, cancellationToken);
    }

    public async Task<IReadOnlyList<JobExecution>> GetExecutionsAsync(
        Guid jobId,
        int page = 0,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    )
    {
        // Fetch enough records to cover the requested page.
        var limit = (page + 1) * pageSize;
        var executions = await storage.GetExecutionsAsync(jobId, limit, cancellationToken).ConfigureAwait(false);

        return executions.OrderByDescending(e => e.ScheduledTime).Skip(page * pageSize).Take(pageSize).ToList();
    }

    public async Task<IReadOnlyList<StatusCountByDate>> GetExecutionGraphDataAsync(
        Guid jobId,
        int days = 7,
        CancellationToken cancellationToken = default
    )
    {
        // Use a high limit to get recent executions for the graph window.
        var executions = await storage
            .GetExecutionsAsync(jobId, limit: 10_000, cancellationToken)
            .ConfigureAwait(false);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        return executions
            .Where(e => e.ScheduledTime >= cutoff)
            .GroupBy(e => new { e.ScheduledTime.Date, e.Status })
            .Select(g => new StatusCountByDate
            {
                Date = new DateTimeOffset(g.Key.Date, TimeSpan.Zero),
                Status = g.Key.Status.ToString(),
                Count = g.Count(),
            })
            .OrderBy(s => s.Date)
            .ThenBy(s => s.Status, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SchedulerStatusSummary> GetSchedulerStatusAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await storage.GetAllJobsAsync(cancellationToken).ConfigureAwait(false);

        var byMachine = jobs.Where(j => j.Status == ScheduledJobStatus.Running && !string.IsNullOrEmpty(j.LockHolder))
            .GroupBy(j => j.LockHolder!, StringComparer.Ordinal)
            .Select(g => new MachineJobCount { Machine = g.Key, RunningCount = g.Count() })
            .ToList();

        return new SchedulerStatusSummary
        {
            TotalJobs = jobs.Count,
            RunningJobs = jobs.Count(j => j.Status == ScheduledJobStatus.Running),
            PendingJobs = jobs.Count(j => j.Status == ScheduledJobStatus.Pending),
            DisabledJobs = jobs.Count(j => !j.IsEnabled),
            JobsByMachine = byMachine,
        };
    }
}
