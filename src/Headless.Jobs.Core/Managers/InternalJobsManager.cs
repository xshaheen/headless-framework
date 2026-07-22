// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Managers;

internal sealed class InternalJobsManager<TTimeJob, TCronJob>(
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    TimeProvider timeProvider,
    IJobsNotificationHubSender notificationHubSender,
    CronScheduleCache cronScheduleCache,
    ILogger<InternalJobsManager<TTimeJob, TCronJob>> logger,
    JobsRequestSerializationOptions serializationOptions,
    IGuidGenerator guidGenerator,
    IServiceProvider serviceProvider
) : IInternalJobManager
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public async Task<(TimeSpan TimeRemaining, JobExecutionState[] Functions)> GetNextJobs(
        CancellationToken cancellationToken = default
    )
    {
        // U5/KTD3 poll-time safety net: skip (never release) idle timed children whose parent terminalized through a
        // path that missed the per-parent / set-based reconcile, so a missed terminalization can never permanently
        // strand a timed child. The skip side never makes a child eligible early, so running it before the peek is
        // safe — it can only remove candidates that must never run. Best-effort: a failure here must NOT block normal
        // scheduling; the fallback loop's set-based reconcile guarantees liveness regardless.
        try
        {
            await persistenceProvider.SkipStrandedTimedChildrenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable ERP022 // The backstop is intentionally non-fatal to the scheduling poll (logged, not rethrown).
        catch (Exception exception)
        {
            logger.LogTimedChildSafetyNetFailed(exception);
        }
#pragma warning restore ERP022

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
            RetryCount = timeJob.RetryCount,
            RetryIntervals = timeJob.RetryIntervals,
            ParentId = timeJob.ParentId,
            ExecutionTime = timeJob.ExecutionTime ?? timeProvider.GetUtcNow().UtcDateTime,
        };

        // The provider already hydrated the tree bounded to MaxChainDepth (U3); recurse the whole thing so a chain
        // deeper than the grandchild level is executed with each descendant's own RunCondition/RetryCount intact
        // (omitting RetryCount here would reset the retry budget after restart — docs/solutions precedent).
        foreach (var child in timeJob.Children)
        {
            context.TimeJobChildren.Add(_BuildQueuedTimeJobChildContext(child));
        }

        return context;
    }

    private static JobExecutionState _BuildQueuedTimeJobChildContext(TimeJobEntity child)
    {
        var childContext = new JobExecutionState
        {
            FunctionName = child.Function,
            JobId = child.Id,
            Type = JobType.TimeJob,
            Retries = child.Retries,
            RetryCount = child.RetryCount,
            RetryIntervals = child.RetryIntervals,
            ParentId = child.ParentId,
            RunCondition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus,
        };

        foreach (var grandChild in child.Children)
        {
            childContext.TimeJobChildren.Add(_BuildQueuedTimeJobChildContext(grandChild));
        }

        return childContext;
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
                    RetryCount = occurrence.RetryCount,
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
            if (cronJob.IsPaused)
            {
                continue;
            }

            var next = cronScheduleCache.GetNextOccurrenceOrDefault(cronJob.Expression, now, cronJob.TimeZoneId);
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
                    TimeZoneId = cronJob.TimeZoneId,
                    IsPaused = cronJob.IsPaused,
                    ScheduleRevision = cronJob.ScheduleRevision,
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
                        TimeZoneId = cronJob.TimeZoneId,
                        IsPaused = cronJob.IsPaused,
                        ScheduleRevision = cronJob.ScheduleRevision,
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
                TimeZoneId = earliestStored.CronJob.TimeZoneId,
                IsPaused = earliestStored.CronJob.IsPaused,
                ScheduleRevision = earliestStored.CronJob.ScheduleRevision,
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

    public async Task<JobExecutionState[]> SetTickersInProgress(
        JobExecutionState[] resources,
        CancellationToken cancellationToken = default
    )
    {
        var unifiedFunctionContext = new JobExecutionState { FunctionName = string.Empty }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );

        var cronJobIds = resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();
        var timeJobIds = resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        Guid[] stampedCronJobIds = [];
        Guid[] stampedTimeJobIds = [];

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
            stampedCronJobIds = await updateCronJobOccurrencesTask.ConfigureAwait(false);
            stampedTimeJobIds = await updateTimeJobsTask.ConfigureAwait(false);
        }
        else
        {
            if (cronJobIds.Length != 0)
            {
                stampedCronJobIds = await persistenceProvider
                    .UpdateCronJobOccurrencesWithUnifiedContextAsync(
                        cronJobIds,
                        unifiedFunctionContext,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (timeJobIds.Length != 0)
            {
                stampedTimeJobIds = await persistenceProvider
                    .UpdateTimeJobsWithUnifiedContextAsync(timeJobIds, unifiedFunctionContext, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var stampedCronJobIdSet = new HashSet<Guid>(stampedCronJobIds);
        var stampedTimeJobIdSet = new HashSet<Guid>(stampedTimeJobIds);
        var stampedResources = resources
            .Where(resource =>
                resource.Type == JobType.TimeJob
                    ? stampedTimeJobIdSet.Contains(resource.JobId)
                    : stampedCronJobIdSet.Contains(resource.JobId)
            )
            .ToArray();

        foreach (var resource in stampedResources)
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

        return stampedResources;
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

    public async Task<bool> RequestTimeJobCancellationAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var accepted = await persistenceProvider
            .RequestTimeJobCancellationAsync(jobId, cancellationToken)
            .ConfigureAwait(false);
        if (!accepted)
        {
            return false;
        }

        try
        {
            await notificationHubSender.CanceledJobNotifyAsync(jobId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogDurableCancellationNotificationFailed(exception, jobId);
        }

        // U5/KTD3: reconcile the cancelled parent's TIMED children through the same reconcile+wake path as the executor,
        // so a released matching child (OnCancelled/OnFailureOrCancelled/OnAnyCompletedStatus) is claimed promptly via
        // RestartIfNeeded instead of waiting for the fallback tick, and non-matching timed children are skipped with
        // their subtree. A running (not-yet-terminal) parent makes this a no-op — the executor reconciles it when it
        // later reaches Cancelled.
        //
        // The cancellation is already committed, so this post-commit reconcile is a recoverable side-effect (the
        // poll-time safety net / set-based sweep reconcile any miss): a failure here must NOT fail the accepted
        // cancellation. CancellationToken.None mirrors the executor's post-commit reconcile — the committed
        // cancellation's follow-up must not be torn down by the caller's token.
        try
        {
            await ApplyParentTerminalRunConditionsAsync(jobId, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Non-fatal post-commit side effect: logged, not rethrown (backstops reconcile any miss).
        catch (Exception exception)
        {
            logger.LogTimedChildReconcileAfterCancellationFailed(exception, jobId);
        }
#pragma warning restore ERP022

        return true;
    }

    public Task<bool?> IsTimeJobCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        persistenceProvider.IsTimeJobCancellationRequestedAsync(jobId, cancellationToken);

    public async Task<bool> PauseCronJobAsync(Guid cronJobId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await persistenceProvider
            .PauseCronJobAsync(cronJobId, now, cancellationToken)
            .ConfigureAwait(false);

        return await _PublishAcceptedCronControlAsync(updated, "pause").ConfigureAwait(false);
    }

    public async Task<bool> ResumeCronJobAsync(Guid cronJobId, CancellationToken cancellationToken = default)
    {
        var definition = await persistenceProvider
            .GetCronJobByIdAsync(cronJobId, cancellationToken)
            .ConfigureAwait(false);
        if (definition?.IsPaused != true)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var next = cronScheduleCache.GetNextOccurrenceOrDefault(definition.Expression, now, definition.TimeZoneId);
        if (next is null)
        {
            return false;
        }

        var occurrence = CronJobOccurrenceFactory.Create(definition, next.Value, now, guidGenerator);
        var updated = await persistenceProvider
            .ResumeCronJobAsync(definition.Id, definition.ScheduleRevision, occurrence, now, cancellationToken)
            .ConfigureAwait(false);

        return await _PublishAcceptedCronControlAsync(updated, "resume").ConfigureAwait(false);
    }

    private async Task<bool> _PublishAcceptedCronControlAsync(TCronJob? updated, string operation)
    {
        if (updated is null)
        {
            return false;
        }

        try
        {
            await notificationHubSender.UpdateCronJobNotifyAsync(updated).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogCronControlNotificationFailed(exception, updated.Id, operation);
        }

        return true;
    }

    public async Task UpdateSkipTimeJobsWithUnifiedContextAsync(
        JobExecutionState[] resources,
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var unifiedFunctionContext = new JobExecutionState { FunctionName = string.Empty }
            .SetProperty(x => x.Status, JobStatus.Skipped)
            .SetProperty(x => x.ExecutedAt, now)
            .SetProperty(x => x.ExceptionDetails, ChainRunConditionRules.RunConditionMismatchReason);

        var cronJobIds = resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();
        var timeJobIds = resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        Guid[] skippedCronJobIds = [];
        Guid[] skippedTimeJobIds = [];

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
            skippedCronJobIds = await updateCronJobOccurrencesTask.ConfigureAwait(false);
            skippedTimeJobIds = await updateTimeJobsTask.ConfigureAwait(false);
        }
        else
        {
            if (cronJobIds.Length != 0)
            {
                skippedCronJobIds = await persistenceProvider
                    .UpdateCronJobOccurrencesWithUnifiedContextAsync(
                        cronJobIds,
                        unifiedFunctionContext,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (timeJobIds.Length != 0)
            {
                skippedTimeJobIds = await persistenceProvider
                    .UpdateTimeJobsWithUnifiedContextAsync(timeJobIds, unifiedFunctionContext, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var skippedCronJobIdSet = new HashSet<Guid>(skippedCronJobIds);
        var skippedTimeJobIdSet = new HashSet<Guid>(skippedTimeJobIds);
        var skippedResources = resources
            .Where(resource =>
                resource.Type == JobType.TimeJob
                    ? skippedTimeJobIdSet.Contains(resource.JobId)
                    : skippedCronJobIdSet.Contains(resource.JobId)
            )
            .ToArray();

        foreach (var resource in skippedResources)
        {
            resource.ExecutedAt = now;
            resource.Status = JobStatus.Skipped;
            resource.ExceptionDetails = ChainRunConditionRules.RunConditionMismatchReason;
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

        return request == null ? default : JobsHelper.ReadJobRequest<T>(request, serializationOptions);
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
                RetryCount = timedOutCronJob.RetryCount,
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
    )
    {
        await persistenceProvider.MigrateDefinedCronJobsAsync(cronExpressions, cancellationToken).ConfigureAwait(false);
    }

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

        // U5/KTD3: the dead-node sweep terminalizes parents in bulk (MarkFailed/Skip) and reports only counts, so a
        // per-parent reconcile cannot reach them — reconcile every terminal parent's timed children set-based here.
        await _ReconcileAllTerminalTimedChildrenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ReclaimStalledResources(CancellationToken cancellationToken = default)
    {
        var timeJobsTask = persistenceProvider.ReclaimStalledTimeJobsAsync(cancellationToken);
        var cronOccurrencesTask = persistenceProvider.ReclaimStalledCronJobOccurrencesAsync(cancellationToken);

        // WhenAll of two Task<int> yields the results array in one await — concurrent, no double-await, and a double
        // fault surfaces as AggregateException rather than collapsing to the first task's exception.
        var results = await Task.WhenAll(timeJobsTask, cronOccurrencesTask).ConfigureAwait(false);

        // U5/KTD3: the stalled-lease sweep terminalizes parents in bulk (reporting only counts), so reconcile every
        // terminal parent's timed children set-based right after — release matching (re-stamp past-due) / skip
        // non-matching + subtree — mirroring the dead-node path.
        await _ReconcileAllTerminalTimedChildrenAsync(cancellationToken).ConfigureAwait(false);

        return results[0] + results[1];
    }

    public async Task ApplyParentTerminalRunConditionsAsync(
        Guid parentId,
        CancellationToken cancellationToken = default
    )
    {
        // U5/KTD3 per-parent reconcile, invoked after a parent's terminal write committed (executor / cancellation).
        await _ApplyTerminalRunConditionsAndWakeAsync(parentId, cancellationToken).ConfigureAwait(false);
    }

    private async Task _ReconcileAllTerminalTimedChildrenAsync(CancellationToken cancellationToken)
    {
        await _ApplyTerminalRunConditionsAndWakeAsync(parentId: null, cancellationToken).ConfigureAwait(false);
    }

    // Runs the provider reconcile (per-parent when parentId is set, every terminal parent when null) and wakes the
    // scheduler for the earliest released child, if any.
    private async Task _ApplyTerminalRunConditionsAndWakeAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        var earliest = await persistenceProvider
            .ApplyParentTerminalRunConditionsAsync(parentId, cancellationToken)
            .ConfigureAwait(false);

        _WakeSchedulerForReleasedChild(earliest);
    }

    private void _WakeSchedulerForReleasedChild(DateTime? earliestReleasedTime)
    {
        if (earliestReleasedTime is null)
        {
            return;
        }

        // Resolve the host scheduler lazily to break the JobsSchedulerBackgroundService (IJobsHostScheduler) ⇄
        // IInternalJobManager constructor cycle. RestartIfNeeded runs only AFTER the releasing transaction committed
        // (a pre-commit nudge would wake the scheduler into pre-commit state and it would sleep again — KTD3).
        serviceProvider.GetService<IJobsHostScheduler>()?.RestartIfNeeded(earliestReleasedTime);
    }
}

internal static partial class InternalJobsManagerLog
{
    [LoggerMessage(
        EventId = 3212,
        Level = LogLevel.Warning,
        Message = "Durable cancellation for time job {JobId} was committed, but the dashboard notification failed."
    )]
    public static partial void LogDurableCancellationNotificationFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId
    );

    [LoggerMessage(
        EventId = 3213,
        Level = LogLevel.Warning,
        Message = "Cron definition {CronJobId} {Operation} was committed, but the dashboard notification failed."
    )]
    public static partial void LogCronControlNotificationFailed(
        this ILogger logger,
        Exception exception,
        Guid cronJobId,
        string operation
    );

    [LoggerMessage(
        EventId = 3214,
        Level = LogLevel.Debug,
        Message = "Poll-time timed-descendant safety net failed; any stranded timed children will be reconciled by the "
            + "fallback sweep's set-based reconcile instead. Scheduling continues."
    )]
    public static partial void LogTimedChildSafetyNetFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3215,
        Level = LogLevel.Warning,
        Message = "Durable cancellation for time job {JobId} was committed, but the post-commit timed-descendant "
            + "reconcile failed; its timed children will be reconciled by the poll-time safety net / set-based sweep "
            + "instead."
    )]
    public static partial void LogTimedChildReconcileAfterCancellationFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId
    );
}
