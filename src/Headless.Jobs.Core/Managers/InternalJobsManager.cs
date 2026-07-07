// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Headless.Jobs.Managers;

internal sealed class InternalJobsManager<TTimeJob, TCronJob>(
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    TimeProvider timeProvider,
    IJobsNotificationHubSender notificationHubSender,
    CronScheduleCache cronScheduleCache
) : IInternalJobManager
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public async Task<(TimeSpan TimeRemaining, JobExecutionState[] Functions)> GetNextJobs(
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var minCronGroupTask = _GetEarliestCronJobGroupAsync(cancellationToken);
        var minTimeJobsTask = persistenceProvider.GetEarliestTimeJobsAsync(cancellationToken);

        await Task.WhenAll(minCronGroupTask, minTimeJobsTask).ConfigureAwait(false);

        var minCronGroup = await minCronGroupTask.ConfigureAwait(false);
        var minTimeJobs = await minTimeJobsTask.ConfigureAwait(false);

        var cronTime = minCronGroup?.Key;
        var timeJobTime = minTimeJobs.Length > 0 ? minTimeJobs[0].ExecutionTime : null;

        if (cronTime is null && timeJobTime is null)
        {
            return (Timeout.InfiniteTimeSpan, []);
        }

        TimeSpan timeRemaining;
        var includeCron = false;
        var includeTimeJobs = false;

        if (cronTime is null)
        {
            includeTimeJobs = true;
            timeRemaining = _SafeRemaining(timeJobTime!.Value, now);
        }
        else if (timeJobTime is null)
        {
            includeCron = true;
            timeRemaining = _SafeRemaining(cronTime.Value, now);
        }
        else
        {
            var cronSecond = new DateTime(
                cronTime.Value.Year,
                cronTime.Value.Month,
                cronTime.Value.Day,
                cronTime.Value.Hour,
                cronTime.Value.Minute,
                cronTime.Value.Second
            );
            var timeSecond = new DateTime(
                timeJobTime.Value.Year,
                timeJobTime.Value.Month,
                timeJobTime.Value.Day,
                timeJobTime.Value.Hour,
                timeJobTime.Value.Minute,
                timeJobTime.Value.Second
            );

            if (cronSecond == timeSecond)
            {
                includeCron = true;
                includeTimeJobs = true;
                var earliest = cronTime < timeJobTime ? cronTime.Value : timeJobTime.Value;
                timeRemaining = _SafeRemaining(earliest, now);
            }
            else if (cronTime < timeJobTime)
            {
                includeCron = true;
                timeRemaining = _SafeRemaining(cronTime.Value, now);
            }
            else
            {
                includeTimeJobs = true;
                timeRemaining = _SafeRemaining(timeJobTime.Value, now);
            }
        }

        if (!includeCron && !includeTimeJobs)
        {
            return (Timeout.InfiniteTimeSpan, []);
        }

        JobExecutionState[] cronFunctions = [];
        JobExecutionState[] timeFunctions = [];

        if (includeCron && minCronGroup is not null)
        {
            cronFunctions = await _QueueNextCronJobsAsync(minCronGroup.Value, cancellationToken).ConfigureAwait(false);
        }

        if (includeTimeJobs && minTimeJobs.Length > 0)
        {
            timeFunctions = await _QueueNextTimeJobsAsync(minTimeJobs, cancellationToken).ConfigureAwait(false);
        }

        if (cronFunctions.Length == 0 && timeFunctions.Length == 0)
        {
            return (timeRemaining, []);
        }

        if (cronFunctions.Length == 0)
        {
            return (timeRemaining, timeFunctions);
        }

        if (timeFunctions.Length == 0)
        {
            return (timeRemaining, cronFunctions);
        }

        var merged = new JobExecutionState[cronFunctions.Length + timeFunctions.Length];
        cronFunctions.AsSpan().CopyTo(merged.AsSpan(0, cronFunctions.Length));
        timeFunctions.AsSpan().CopyTo(merged.AsSpan(cronFunctions.Length, timeFunctions.Length));

        return (timeRemaining, merged);
    }

    private static TimeSpan _SafeRemaining(DateTime target, DateTime now)
    {
        var remaining = target - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private async Task<JobExecutionState[]> _QueueNextTimeJobsAsync(
        TimeJobEntity[] minTimeJobs,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<JobExecutionState>();

        await foreach (var updatedTimeJob in persistenceProvider.QueueTimeJobsAsync(minTimeJobs, cancellationToken))
        {
            results.Add(_BuildQueuedTimeJobContext(updatedTimeJob));

            await notificationHubSender.UpdateTimeJobNotifyAsync(updatedTimeJob).ConfigureAwait(false);
        }

        return [.. results];
    }

    private JobExecutionState _BuildQueuedTimeJobContext(TimeJobEntity timeJob)
    {
        var context = new JobExecutionState
        {
            FunctionName = timeJob.Function,
            JobId = timeJob.Id,
            Type = JobType.TimeJob,
            Retries = timeJob.Retries,
            RetryIntervals = timeJob.RetryIntervals,
            ParentId = timeJob.ParentId,
            ExecutionTime = timeJob.ExecutionTime ?? timeProvider.GetUtcNow().UtcDateTime,
        };

        foreach (var child in timeJob.Children)
        {
            var childContext = new JobExecutionState
            {
                FunctionName = child.Function,
                JobId = child.Id,
                Type = JobType.TimeJob,
                Retries = child.Retries,
                RetryIntervals = child.RetryIntervals,
                ParentId = child.ParentId,
                RunCondition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus,
            };

            childContext.TimeJobChildren.AddRange(
                child.Children.Select(grandChild => new JobExecutionState
                {
                    FunctionName = grandChild.Function,
                    JobId = grandChild.Id,
                    Type = JobType.TimeJob,
                    Retries = grandChild.Retries,
                    RetryIntervals = grandChild.RetryIntervals,
                    ParentId = grandChild.ParentId,
                    RunCondition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                })
            );

            context.TimeJobChildren.Add(childContext);
        }

        return context;
    }

    private async Task<JobExecutionState[]> _QueueNextCronJobsAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) minCronJob,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<JobExecutionState>();

        await foreach (
            var occurrence in persistenceProvider
                .QueueCronJobOccurrencesAsync(minCronJob, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(
                new JobExecutionState
                {
                    ParentId = occurrence.CronJobId,
                    FunctionName = occurrence.CronJob.Function,
                    JobId = occurrence.Id,
                    Type = JobType.CronJobOccurrence,
                    Retries = occurrence.CronJob.Retries,
                    RetryIntervals = occurrence.CronJob.RetryIntervals,
                    ExecutionTime = occurrence.ExecutionTime,
                }
            );

            if (occurrence.CreatedAt == occurrence.UpdatedAt && notificationHubSender != null)
            {
                await notificationHubSender
                    .AddCronOccurrenceAsync(occurrence.CronJobId, occurrence)
                    .ConfigureAwait(false);
            }
            else if (notificationHubSender != null)
            {
                await notificationHubSender
                    .UpdateCronOccurrenceAsync(occurrence.CronJobId, occurrence)
                    .ConfigureAwait(false);
            }
        }

        return [.. results];
    }

    private async Task<(DateTime Key, JobManagerDispatchContext[] Items)?> _GetEarliestCronJobGroupAsync(
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var cronJobs = await persistenceProvider.GetAllCronJobExpressionsAsync(cancellationToken).ConfigureAwait(false);

        var cronJobIds = cronJobs.Select(x => x.Id).ToArray();

        var earliestAvailableCronOccurrence = await persistenceProvider
            .GetEarliestAvailableCronOccurrenceAsync(cronJobIds, cancellationToken)
            .ConfigureAwait(false);

        return _EarliestCronJobGroup(cronJobs, now, earliestAvailableCronOccurrence);
    }

    private (DateTime Next, JobManagerDispatchContext[] Items)? _EarliestCronJobGroup(
        CronJobEntity[] cronJobs,
        DateTime now,
        CronJobOccurrenceEntity<TCronJob> earliestStored
    )
    {
        DateTime? min = null;
        JobManagerDispatchContext? first = null;
        List<JobManagerDispatchContext>? ties = null;

        foreach (var cronJob in cronJobs)
        {
            var next = cronScheduleCache.GetNextOccurrenceOrDefault(cronJob.Expression, now);
            if (next is null)
            {
                continue;
            }

            if (
                earliestStored != null
                && earliestStored.ExecutionTime == next
                && cronJob.Id == earliestStored.CronJobId
            )
            {
                continue;
            }

            var n = next.Value;
            if (min is null || n < min)
            {
                min = n;
                first = new JobManagerDispatchContext(cronJob.Id)
                {
                    FunctionName = cronJob.Function,
                    Expression = cronJob.Expression,
                    Retries = cronJob.Retries,
                    RetryIntervals = cronJob.RetryIntervals,
                    OnNodeDeath = cronJob.OnNodeDeath,
                };

                ties = null;
            }
            else if (n == min)
            {
                ties ??= new List<JobManagerDispatchContext>(2) { first! };
                ties.Add(
                    new JobManagerDispatchContext(cronJob.Id)
                    {
                        FunctionName = cronJob.Function,
                        Expression = cronJob.Expression,
                        Retries = cronJob.Retries,
                        RetryIntervals = cronJob.RetryIntervals,
                        OnNodeDeath = cronJob.OnNodeDeath,
                    }
                );
            }
        }

        // If we have a stored occurrence, compare/merge
        if (earliestStored is not null)
        {
            var storedTime = earliestStored.ExecutionTime;
            var storedItem = new JobManagerDispatchContext(earliestStored.CronJobId)
            {
                FunctionName = earliestStored.CronJob.Function,
                Expression = earliestStored.CronJob.Expression,
                Retries = earliestStored.CronJob.Retries,
                RetryIntervals = earliestStored.CronJob.RetryIntervals,
                OnNodeDeath = earliestStored.CronJob.OnNodeDeath,
                NextCronOccurrence = new NextCronOccurrence(earliestStored.Id, earliestStored.CreatedAt),
            };

            // If no in-memory occurrences or stored is earlier, return stored only
            if (min is null || storedTime < min.Value)
            {
                return (storedTime, [storedItem]);
            }

            // If stored time equals the earliest in-memory time, aggregate them
            if (storedTime == min.Value)
            {
                if (ties is null)
                {
                    return (min.Value, [first!, storedItem]);
                }

                ties.Add(storedItem);
                return (min.Value, ties.ToArray());
            }

            // Stored is later than min, return in-memory winners only
            var winners = ties is null ? [first!] : ties.ToArray();
            return (min.Value, winners);
        }

        // No stored occurrence - return in-memory winners or null if none
        if (min is null)
        {
            return null;
        }

        var finalWinners = ties is null ? [first!] : ties.ToArray();
        return (min.Value, finalWinners);
    }

    public async Task SetTickersInProgress(JobExecutionState[] resources, CancellationToken cancellationToken = default)
    {
        var unifiedFunctionContext = new JobExecutionState { FunctionName = string.Empty }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );

        var cronJobIds = resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();
        var timeJobIds = resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        if (cronJobIds.Length != 0 && timeJobIds.Length != 0)
        {
            var updateCronJobOccurrencesTask = persistenceProvider.UpdateCronJobOccurrencesWithUnifiedContextAsync(
                cronJobIds,
                unifiedFunctionContext,
                cancellationToken
            );
            var updateTimeJobsTask = persistenceProvider.UpdateTimeJobsWithUnifiedContextAsync(
                timeJobIds,
                unifiedFunctionContext,
                cancellationToken
            );
            await Task.WhenAll(updateCronJobOccurrencesTask, updateTimeJobsTask).ConfigureAwait(false);
        }
        else
        {
            if (cronJobIds.Length != 0)
            {
                await persistenceProvider
                    .UpdateCronJobOccurrencesWithUnifiedContextAsync(
                        cronJobIds,
                        unifiedFunctionContext,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (timeJobIds.Length != 0)
            {
                await persistenceProvider
                    .UpdateTimeJobsWithUnifiedContextAsync(timeJobIds, unifiedFunctionContext, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var resource in resources)
        {
            resource.Status = JobStatus.InProgress;

            if (resource.Type == JobType.TimeJob)
            {
                await notificationHubSender.UpdateTimeJobFromExecutionState<TTimeJob>(resource).ConfigureAwait(false);
            }
            else
            {
                await notificationHubSender
                    .UpdateCronOccurrenceFromExecutionState<TCronJob>(resource)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task ReleaseAcquiredResources(
        JobExecutionState[]? resources,
        CancellationToken cancellationToken = default
    )
    {
        if (resources is null)
        {
            await Task.WhenAll(
                    persistenceProvider.ReleaseAcquiredCronJobOccurrencesAsync([], cancellationToken),
                    persistenceProvider.ReleaseAcquiredTimeJobsAsync([], cancellationToken)
                )
                .ConfigureAwait(false);
            return;
        }

        var cronJobIds =
            resources.Length == 0
                ? []
                : resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();

        if (cronJobIds.Length != 0)
        {
            await persistenceProvider
                .ReleaseAcquiredCronJobOccurrencesAsync(cronJobIds, cancellationToken)
                .ConfigureAwait(false);
        }

        var timeJobIds =
            resources.Length == 0 ? [] : resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        if (timeJobIds.Length != 0)
        {
            await persistenceProvider.ReleaseAcquiredTimeJobsAsync(timeJobIds, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> UpdateTickerAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        // #462: propagate the affected-row count so a caller completing a job successfully can detect a fenced-out
        // write (0 rows — the row was reclaimed/terminalized by a sweep after a stall) and flag the divergence.
        int affected;
        if (functionContext.Type == JobType.CronJobOccurrence)
        {
            affected = await persistenceProvider
                .UpdateCronJobOccurrenceAsync(functionContext, cancellationToken)
                .ConfigureAwait(false);
            await notificationHubSender
                .UpdateCronOccurrenceFromExecutionState<TCronJob>(functionContext)
                .ConfigureAwait(false);
        }
        else
        {
            affected = await persistenceProvider
                .UpdateTimeJobAsync(functionContext, cancellationToken)
                .ConfigureAwait(false);
            await notificationHubSender
                .UpdateTimeJobFromExecutionState<TTimeJob>(functionContext)
                .ConfigureAwait(false);
        }

        return affected;
    }

    public async Task<int> RenewLeaseAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        return functionContext.Type == JobType.CronJobOccurrence
            ? await persistenceProvider
                .RenewCronJobOccurrenceLeaseAsync(functionContext.JobId, cancellationToken)
                .ConfigureAwait(false)
            : await persistenceProvider
                .RenewTimeJobLeaseAsync(functionContext.JobId, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task UpdateSkipTimeJobsWithUnifiedContextAsync(
        JobExecutionState[] resources,
        CancellationToken cancellationToken = default
    )
    {
        var unifiedFunctionContext = new JobExecutionState { FunctionName = string.Empty }
            .SetProperty(x => x.Status, JobStatus.Skipped)
            .SetProperty(x => x.ExecutedAt, timeProvider.GetUtcNow().UtcDateTime)
            .SetProperty(x => x.ExceptionDetails, "Rule RunCondition did not match!");

        if (resources.Length != 0)
        {
            await persistenceProvider
                .UpdateTimeJobsWithUnifiedContextAsync(
                    [.. resources.Select(x => x.JobId)],
                    unifiedFunctionContext,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var resource in resources)
        {
            resource.ExecutedAt = timeProvider.GetUtcNow().UtcDateTime;
            resource.Status = JobStatus.Skipped;
            resource.ExceptionDetails = "Rule RunCondition did not match!";
            if (resource.Type == JobType.TimeJob)
            {
                await notificationHubSender.UpdateTimeJobFromExecutionState<TTimeJob>(resource).ConfigureAwait(false);
            }
            else
            {
                await notificationHubSender
                    .UpdateCronOccurrenceFromExecutionState<TCronJob>(resource)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task<T?> GetRequestAsync<T>(Guid jobId, JobType type, CancellationToken cancellationToken = default)
    {
        var request =
            type == JobType.CronJobOccurrence
                ? await persistenceProvider
                    .GetCronJobOccurrenceRequestAsync(jobId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : await persistenceProvider
                    .GetTimeJobRequestAsync(jobId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

        return request == null ? default : JobsHelper.ReadJobRequest<T>(request);
    }

    public async Task<JobExecutionState[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
    {
        var results = new List<JobExecutionState>();

        await foreach (
            var timedOutTimeJob in persistenceProvider
                .QueueTimedOutTimeJobsAsync(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(_BuildQueuedTimeJobContext(timedOutTimeJob));

            await notificationHubSender.UpdateTimeJobNotifyAsync(timedOutTimeJob).ConfigureAwait(false);
        }

        await foreach (
            var timedOutCronJob in persistenceProvider
                .QueueTimedOutCronJobOccurrencesAsync(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            var functionContext = new JobExecutionState
            {
                FunctionName = timedOutCronJob.CronJob.Function,
                JobId = timedOutCronJob.Id,
                Type = JobType.CronJobOccurrence,
                Retries = timedOutCronJob.CronJob.Retries,
                RetryIntervals = timedOutCronJob.CronJob.RetryIntervals,
                ParentId = timedOutCronJob.CronJobId,
                ExecutionTime = timedOutCronJob.ExecutionTime,
            };

            results.Add(functionContext);
            await notificationHubSender
                .UpdateCronOccurrenceFromExecutionState<TCronJob>(functionContext)
                .ConfigureAwait(false);
        }

        return [.. results];
    }

    public async Task MigrateDefinedCronJobs(
        (string, string)[] cronExpressions,
        CancellationToken cancellationToken = default
    ) =>
        await persistenceProvider.MigrateDefinedCronJobsAsync(cronExpressions, cancellationToken).ConfigureAwait(false);

    public async Task DeleteJob(Guid jobId, JobType type, CancellationToken cancellationToken = default)
    {
        if (type == JobType.CronJobOccurrence)
        {
            await persistenceProvider.RemoveCronJobsAsync([jobId], cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await persistenceProvider.RemoveTimeJobsAsync([jobId], cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var cronOccurrence = persistenceProvider.ReleaseDeadNodeOccurrenceResourcesAsync(
            instanceIdentifier,
            cancellationToken
        );

        var timeJobs = persistenceProvider.ReleaseDeadNodeTimeJobResourcesAsync(instanceIdentifier, cancellationToken);

        await Task.WhenAll(cronOccurrence, timeJobs).ConfigureAwait(false);
    }

    public async Task<int> ReclaimStalledResources(CancellationToken cancellationToken = default)
    {
        var timeJobsTask = persistenceProvider.ReclaimStalledTimeJobsAsync(cancellationToken);
        var cronOccurrencesTask = persistenceProvider.ReclaimStalledCronJobOccurrencesAsync(cancellationToken);

        // WhenAll of two Task<int> yields the results array in one await — concurrent, no double-await, and a double
        // fault surfaces as AggregateException rather than collapsing to the first task's exception.
        var results = await Task.WhenAll(timeJobsTask, cronOccurrencesTask).ConfigureAwait(false);

        return results[0] + results[1];
    }
}
