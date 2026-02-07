// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Scheduling;

internal sealed class ScheduledJobManager(
    IScheduledJobStorage storage,
    CronScheduleCache cronCache,
    TimeProvider timeProvider
) : IScheduledJobManager
{
    public Task<IReadOnlyList<ScheduledJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return storage.GetAllJobsAsync(cancellationToken);
    }

    public Task<ScheduledJob?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        return storage.GetJobByNameAsync(name, cancellationToken);
    }

    public async Task EnableAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job =
            await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scheduled job '{name}' not found.");

        var now = timeProvider.GetUtcNow();
        var nextRun = cronCache.GetNextOccurrence(job.CronExpression!, job.TimeZone, now);

        job.Status = ScheduledJobStatus.Pending;
        job.IsEnabled = true;
        job.NextRunTime = nextRun;
        job.DateUpdated = now;

        await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisableAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job =
            await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scheduled job '{name}' not found.");

        job.Status = ScheduledJobStatus.Disabled;
        job.IsEnabled = false;
        job.NextRunTime = null;
        job.DateUpdated = timeProvider.GetUtcNow();

        await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task TriggerAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job =
            await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scheduled job '{name}' not found.");

        job.Status = ScheduledJobStatus.Pending;
        job.NextRunTime = timeProvider.GetUtcNow();
        job.DateUpdated = timeProvider.GetUtcNow();

        await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job =
            await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scheduled job '{name}' not found.");

        await storage.DeleteJobAsync(job.Id, cancellationToken).ConfigureAwait(false);
    }
}
