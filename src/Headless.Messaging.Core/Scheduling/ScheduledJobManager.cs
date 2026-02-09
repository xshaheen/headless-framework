// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

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

    public async Task<Result<ResultError>> EnableAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job = await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return _JobNotFound(name);
        }

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
        return Result<ResultError>.Ok();
    }

    public async Task<Result<ResultError>> DisableAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job = await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return _JobNotFound(name);
        }

        job.Status = ScheduledJobStatus.Disabled;
        job.IsEnabled = false;
        job.NextRunTime = null;
        job.DateUpdated = timeProvider.GetUtcNow();

        await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
        return Result<ResultError>.Ok();
    }

    public async Task<Result<ResultError>> TriggerAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job = await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return _JobNotFound(name);
        }

        var now = timeProvider.GetUtcNow();
        job.Status = ScheduledJobStatus.Pending;
        job.NextRunTime = now;
        job.DateUpdated = now;

        await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
        return Result<ResultError>.Ok();
    }

    public async Task<Result<ResultError>> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(name);

        var job = await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return _JobNotFound(name);
        }

        await storage.DeleteJobAsync(job.Id, cancellationToken).ConfigureAwait(false);
        return Result<ResultError>.Ok();
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
            MaxRetries = 0,
            SkipIfRunning = false,
            IsEnabled = true,
            DateCreated = now,
            DateUpdated = now,
            MisfireStrategy = MisfireStrategy.FireImmediately,
            ConsumerTypeName = consumerType.AssemblyQualifiedName,
        };

        await storage.UpsertJobAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private static NotFoundError _JobNotFound(string name) => new() { Entity = "ScheduledJob", Key = name };
}
