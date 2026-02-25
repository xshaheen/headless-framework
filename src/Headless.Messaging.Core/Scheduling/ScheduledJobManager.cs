// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using System.Text.Json;

namespace Headless.Messaging.Scheduling;

internal sealed class ScheduledJobManager(
    IScheduledJobStorage storage,
    CronScheduleCache cronCache,
    TimeProvider timeProvider
) : IScheduledJobManager
{
    public Task<IReadOnlyList<ScheduledJob>> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        return storage.GetAllJobsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobExecution>> ListExecutionsAsync(
        string name,
        int limit = 20,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(name);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        var job = await storage.GetJobByNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return [];
        }

        return await storage.GetExecutionsAsync(job.Id, limit, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ScheduledJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ListJobsAsync(cancellationToken);
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

        if (job is { Type: ScheduledJobType.Recurring, CronExpression: not null })
        {
            job.NextRunTime = cronCache.GetNextOccurrence(job.CronExpression, job.TimeZone, now);
        }
        else
        {
            // OneTime jobs without a scheduled time re-enable with immediate execution
            job.NextRunTime ??= now;
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
            throw new ArgumentException(@"Run time must be in the future.", nameof(runAt));
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

    public Task ScheduleOnceAsync<TConsumer>(
        string name,
        DateTimeOffset runAt,
        string? payload = null,
        CancellationToken cancellationToken = default
    )
        where TConsumer : class, IConsume<ScheduledTrigger>
    {
        return ScheduleOnceAsync(name, runAt, typeof(TConsumer), payload, cancellationToken);
    }

    public Task ScheduleOnceAsync<TConsumer, TPayload>(
        string name,
        DateTimeOffset runAt,
        TPayload payload,
        CancellationToken cancellationToken = default
    )
        where TConsumer : class, IConsume<ScheduledTrigger>
    {
        string? serializedPayload;
        if (payload is null)
        {
            serializedPayload = null;
        }
        else if (payload is string rawPayload)
        {
            serializedPayload = rawPayload;
        }
        else
        {
            serializedPayload = JsonSerializer.Serialize(payload);
        }

        return ScheduleOnceAsync<TConsumer>(name, runAt, serializedPayload, cancellationToken);
    }

    private static NotFoundError _JobNotFound(string name) => new() { Entity = "ScheduledJob", Key = name };
}
