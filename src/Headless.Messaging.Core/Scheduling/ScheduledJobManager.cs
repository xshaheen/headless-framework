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

        job.Status = ScheduledJobStatus.Pending;
        job.IsEnabled = true;
        job.DateUpdated = now;

        if (job.Type == ScheduledJobType.Recurring && job.CronExpression is not null)
        {
            job.NextRunTime = cronCache.GetNextOccurrence(job.CronExpression, job.TimeZone, now);
        }
        else if (job.NextRunTime is null)
        {
            // OneTime jobs without a scheduled time re-enable with immediate execution
            job.NextRunTime = now;
        }

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

        var now = timeProvider.GetUtcNow();
        job.Status = ScheduledJobStatus.Pending;
        job.NextRunTime = now;
        job.DateUpdated = now;

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

    public async Task ScheduleOnceAsync(
        string name,
        DateTimeOffset runAt,
        Type consumerType,
        string? payload = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(name);
        Argument.IsNotNull(consumerType);

        var now = timeProvider.GetUtcNow();

        if (runAt <= now)
        {
            throw new ArgumentException("Run time must be in the future.", nameof(runAt));
        }

        var job = new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = ScheduledJobType.OneTime,
            TimeZone = "UTC",
            Payload = payload,
            Status = ScheduledJobStatus.Pending,
            NextRunTime = runAt,
            RetryCount = 0,
            SkipIfRunning = false,
            IsEnabled = true,
            DateCreated = now,
            DateUpdated = now,
            MisfireStrategy = MisfireStrategy.FireImmediately,
            ConsumerTypeName = consumerType.AssemblyQualifiedName,
        };

        await storage.UpsertJobAsync(job, cancellationToken).ConfigureAwait(false);
    }
}
