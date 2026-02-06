// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Reconciles discovered <see cref="ScheduledJobDefinition"/> instances with persistent storage
/// on application startup. Inserts new jobs, updates changed ones, and soft-disables removed ones.
/// </summary>
internal sealed class SchedulerJobReconciler(
    ScheduledJobDefinitionRegistry definitionRegistry,
    IScheduledJobStorage storage,
    CronScheduleCache cronCache,
    TimeProvider timeProvider,
    ILogger<SchedulerJobReconciler> logger
) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var definitions = definitionRegistry.GetAll();

        if (definitions.Count == 0)
        {
            return;
        }

        logger.LogInformation("Reconciling {Count} scheduled job definition(s)", definitions.Count);

        var now = timeProvider.GetUtcNow();
        var knownNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            knownNames.Add(definition.Name);

            var nextRun = cronCache.GetNextOccurrence(definition.CronExpression, definition.TimeZone, now);

            var job = new ScheduledJob
            {
                Id = Guid.NewGuid(),
                Name = definition.Name,
                Type = ScheduledJobType.Recurring,
                CronExpression = definition.CronExpression,
                TimeZone = definition.TimeZone ?? "UTC",
                Status = ScheduledJobStatus.Pending,
                NextRunTime = nextRun,
                RetryCount = 0,
                RetryIntervals = definition.RetryIntervals,
                SkipIfRunning = definition.SkipIfRunning,
                IsEnabled = true,
                DateCreated = now,
                DateUpdated = now,
            };

            await storage.UpsertJobAsync(job, cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Reconciled job '{JobName}' with cron '{Cron}', next run: {NextRun}",
                definition.Name,
                definition.CronExpression,
                nextRun
            );
        }

        // Soft-disable jobs in DB that are no longer in code
        var allJobs = await storage.GetAllJobsAsync(cancellationToken).ConfigureAwait(false);

        foreach (var existingJob in allJobs)
        {
            if (
                existingJob.Type == ScheduledJobType.Recurring
                && existingJob.IsEnabled
                && !knownNames.Contains(existingJob.Name)
            )
            {
                existingJob.IsEnabled = false;
                existingJob.Status = ScheduledJobStatus.Disabled;
                existingJob.NextRunTime = null;
                existingJob.DateUpdated = now;

                await storage.UpdateJobAsync(existingJob, cancellationToken).ConfigureAwait(false);

                logger.LogInformation("Disabled orphaned job '{JobName}' (no longer in code)", existingJob.Name);
            }
        }
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
