// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Background service that polls for due scheduled jobs and dispatches them for execution.
/// </summary>
internal sealed class SchedulerBackgroundService(
    IScheduledJobStorage storage,
    IScheduledJobDispatcher dispatcher,
    CronScheduleCache cronCache,
    TimeProvider timeProvider,
    ILogger<SchedulerBackgroundService> logger,
    IOptions<SchedulerOptions> options,
    IServiceProvider serviceProvider
) : BackgroundService
{
    private readonly SchedulerOptions _options = options.Value;

    // Null when no distributed lock provider is registered (optional dependency).
    private readonly IDistributedLockProvider? _lockProvider = serviceProvider.GetService<IDistributedLockProvider>();

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await storage
                    .AcquireDueJobsAsync(_options.BatchSize, _options.LockHolder, stoppingToken)
                    .ConfigureAwait(false);

                foreach (var job in jobs)
                {
                    await _ProcessJobAsync(job, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Scheduler loop must not crash on transient errors
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Scheduler polling error");
            }

            await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task _ProcessJobAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        // When a distributed lock provider is available and the job skips overlapping runs,
        // acquire a cross-instance lock before dispatching. If the lock cannot be obtained
        // (another instance is already running this job), skip this occurrence.
        if (_lockProvider is not null && job.SkipIfRunning)
        {
            var distributedLock = await _lockProvider
                .TryAcquireAsync(
                    $"messaging:job:{job.Name}",
                    _options.LockTimeout,
                    TimeSpan.Zero, // don't wait — skip immediately if locked
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                logger.LogDebug("Job {JobName} skipped — already running on another instance", job.Name);

                job.Status = ScheduledJobStatus.Pending;
                job.LockHolder = null;
                job.LockedAt = null;
                await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await _ExecuteJobAsync(job, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await distributedLock.ReleaseAsync().ConfigureAwait(false);
            }
        }
        else
        {
            await _ExecuteJobAsync(job, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _ExecuteJobAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ScheduledTime = job.NextRunTime!.Value,
            StartedAt = timeProvider.GetUtcNow(),
            Status = JobExecutionStatus.Running,
            RetryAttempt = job.RetryCount,
        };

        await storage.CreateExecutionAsync(execution, cancellationToken).ConfigureAwait(false);

        try
        {
            await dispatcher.DispatchAsync(job, execution, cancellationToken).ConfigureAwait(false);

            execution.CompletedAt = timeProvider.GetUtcNow();
            execution.Status = JobExecutionStatus.Succeeded;
            execution.Duration = (long)(execution.CompletedAt.Value - execution.StartedAt!.Value).TotalMilliseconds;

            await storage.UpdateExecutionAsync(execution, cancellationToken).ConfigureAwait(false);

            job.LastRunTime = execution.CompletedAt;
            job.LastRunDuration = execution.Duration;
            job.RetryCount = 0;
            job.Status = ScheduledJobStatus.Pending;
            job.LockHolder = null;
            job.LockedAt = null;

            if (job.Type == ScheduledJobType.Recurring && job.CronExpression is not null)
            {
                job.NextRunTime = cronCache.GetNextOccurrence(
                    job.CronExpression,
                    job.TimeZone,
                    timeProvider.GetUtcNow()
                );
            }
            else
            {
                job.NextRunTime = null;
                job.Status = ScheduledJobStatus.Completed;
            }

            await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Individual job failures must not crash the scheduler
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Job {JobName} execution failed", job.Name);

            execution.CompletedAt = timeProvider.GetUtcNow();
            execution.Status = JobExecutionStatus.Failed;
            execution.Duration = (long)(execution.CompletedAt.Value - execution.StartedAt!.Value).TotalMilliseconds;
            execution.Error = ex.ToString();

            await storage.UpdateExecutionAsync(execution, cancellationToken).ConfigureAwait(false);

            job.RetryCount++;
            job.LockHolder = null;
            job.LockedAt = null;
            job.LastRunTime = execution.CompletedAt;
            job.LastRunDuration = execution.Duration;

            if (job.RetryIntervals is { Length: > 0 } retryIntervals)
            {
                var retryIndex = Math.Min(job.RetryCount - 1, retryIntervals.Length - 1);
                var delayMs = retryIntervals[retryIndex];
                job.NextRunTime = timeProvider.GetUtcNow().AddMilliseconds(delayMs);
                job.Status = ScheduledJobStatus.Pending;
            }
            else
            {
                job.Status = ScheduledJobStatus.Failed;

                // For recurring jobs, compute next regular occurrence despite failure
                if (job.Type == ScheduledJobType.Recurring && job.CronExpression is not null)
                {
                    job.NextRunTime = cronCache.GetNextOccurrence(
                        job.CronExpression,
                        job.TimeZone,
                        timeProvider.GetUtcNow()
                    );
                    job.Status = ScheduledJobStatus.Pending;
                    job.RetryCount = 0;
                }
            }

            await storage.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
        }
    }
}
