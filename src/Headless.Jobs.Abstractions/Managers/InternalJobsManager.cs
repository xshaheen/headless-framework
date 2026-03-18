using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Headless.Jobs.Managers;

internal class InternalJobsManager<TTimeJob, TCronJob> : IInternalJobManager
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IJobPersistenceProvider<TTimeJob, TCronJob> _persistenceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly IJobsNotificationHubSender _notificationHubSender;

    public InternalJobsManager(
        IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
        TimeProvider timeProvider,
        IJobsNotificationHubSender notificationHubSender
    )
    {
        _persistenceProvider = persistenceProvider;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _notificationHubSender = notificationHubSender;
    }

    public async Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextJobs(
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var minCronGroupTask = _GetEarliestCronJobGroupAsync(cancellationToken);
        var minTimeJobsTask = _persistenceProvider.GetEarliestTimeJobs(cancellationToken);

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
        bool includeCron = false;
        bool includeTimeJobs = false;

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

        InternalFunctionContext[] cronFunctions = [];
        InternalFunctionContext[] timeFunctions = [];

        if (includeCron && minCronGroup is not null)
        {
            cronFunctions = await _QueueNextCronJobsAsync(minCronGroup.Value, cancellationToken)
                .ConfigureAwait(false);
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

        var merged = new InternalFunctionContext[cronFunctions.Length + timeFunctions.Length];
        cronFunctions.AsSpan().CopyTo(merged.AsSpan(0, cronFunctions.Length));
        timeFunctions.AsSpan().CopyTo(merged.AsSpan(cronFunctions.Length, timeFunctions.Length));

        return (timeRemaining, merged);
    }

    private static TimeSpan _SafeRemaining(DateTime target, DateTime now)
    {
        var remaining = target - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private async Task<InternalFunctionContext[]> _QueueNextTimeJobsAsync(
        TimeJobEntity[] minTimeJobs,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<InternalFunctionContext>();

        await foreach (
            var updatedTimeJob in _persistenceProvider.QueueTimeJobs(minTimeJobs, cancellationToken)
        )
        {
            results.Add(
                new InternalFunctionContext
                {
                    FunctionName = updatedTimeJob.Function,
                    JobId = updatedTimeJob.Id,
                    Type = JobType.TimeJob,
                    Retries = updatedTimeJob.Retries,
                    RetryIntervals = updatedTimeJob.RetryIntervals,
                    ParentId = updatedTimeJob.ParentId,
                    ExecutionTime = updatedTimeJob.ExecutionTime ?? _timeProvider.GetUtcNow().UtcDateTime,
                    TimeJobChildren = updatedTimeJob
                        .Children.Select(ch => new InternalFunctionContext
                        {
                            FunctionName = ch.Function,
                            JobId = ch.Id,
                            Type = JobType.TimeJob,
                            Retries = ch.Retries,
                            RetryIntervals = ch.RetryIntervals,
                            ParentId = ch.ParentId,
                            RunCondition = ch.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                            TimeJobChildren = ch
                                .Children.Select(gch => new InternalFunctionContext
                                {
                                    FunctionName = gch.Function,
                                    JobId = gch.Id,
                                    Type = JobType.TimeJob,
                                    Retries = gch.Retries,
                                    RetryIntervals = gch.RetryIntervals,
                                    ParentId = gch.ParentId,
                                    RunCondition = ch.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                                })
                                .ToList(),
                        })
                        .ToList(),
                }
            );

            await _notificationHubSender.UpdateTimeJobNotifyAsync(updatedTimeJob);
        }

        return results.ToArray();
    }

    private async Task<InternalFunctionContext[]> _QueueNextCronJobsAsync(
        (DateTime Key, InternalManagerContext[] Items) minCronJob,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<InternalFunctionContext>();

        await foreach (
            var occurrence in _persistenceProvider
                .QueueCronJobOccurrences(minCronJob, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(
                new InternalFunctionContext
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

            if (occurrence.CreatedAt == occurrence.UpdatedAt && _notificationHubSender != null)
            {
                await _notificationHubSender
                    .AddCronOccurrenceAsync(occurrence.CronJobId, occurrence)
                    .ConfigureAwait(false);
            }
            else if (_notificationHubSender != null)
            {
                await _notificationHubSender
                    .UpdateCronOccurrenceAsync(occurrence.CronJobId, occurrence)
                    .ConfigureAwait(false);
            }
        }

        return results.ToArray();
    }

    private async Task<(DateTime Key, InternalManagerContext[] Items)?> _GetEarliestCronJobGroupAsync(
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var cronJobs = await _persistenceProvider
            .GetAllCronJobExpressions(cancellationToken)
            .ConfigureAwait(false);

        var cronJobIds = cronJobs.Select(x => x.Id).ToArray();

        var earliestAvailableCronOccurrence = await _persistenceProvider
            .GetEarliestAvailableCronOccurrence(cronJobIds, cancellationToken)
            .ConfigureAwait(false);

        return _EarliestCronJobGroup(cronJobs, now, earliestAvailableCronOccurrence);
    }

    private static (DateTime Next, InternalManagerContext[] Items)? _EarliestCronJobGroup(
        CronJobEntity[] cronJobs,
        DateTime now,
        CronJobOccurrenceEntity<TCronJob> earliestStored
    )
    {
        DateTime? min = null;
        InternalManagerContext? first = null;
        List<InternalManagerContext>? ties = null;

        foreach (var cronJob in cronJobs)
        {
            var next = CronScheduleCache.GetNextOccurrenceOrDefault(cronJob.Expression, now);
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
                first = new InternalManagerContext(cronJob.Id)
                {
                    FunctionName = cronJob.Function,
                    Expression = cronJob.Expression,
                    Retries = cronJob.Retries,
                    RetryIntervals = cronJob.RetryIntervals,
                };

                ties = null;
            }
            else if (n == min)
            {
                ties ??= new List<InternalManagerContext>(2) { first! };
                ties.Add(
                    new InternalManagerContext(cronJob.Id)
                    {
                        FunctionName = cronJob.Function,
                        Expression = cronJob.Expression,
                        Retries = cronJob.Retries,
                        RetryIntervals = cronJob.RetryIntervals,
                    }
                );
            }
        }

        // If we have a stored occurrence, compare/merge
        if (earliestStored is not null)
        {
            var storedTime = earliestStored.ExecutionTime;
            var storedItem = new InternalManagerContext(earliestStored.CronJobId)
            {
                FunctionName = earliestStored.CronJob.Function,
                Expression = earliestStored.CronJob.Expression,
                Retries = earliestStored.CronJob.Retries,
                RetryIntervals = earliestStored.CronJob.RetryIntervals,
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

    public async Task SetTickersInProgress(
        InternalFunctionContext[] resources,
        CancellationToken cancellationToken = default
    )
    {
        var unifiedFunctionContext = new InternalFunctionContext { FunctionName = string.Empty }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );

        var cronJobIds = resources
            .Where(x => x.Type == JobType.CronJobOccurrence)
            .Select(x => x.JobId)
            .ToArray();
        var timeJobIds = resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        if (cronJobIds.Length != 0 && timeJobIds.Length != 0)
        {
            var updateCronJobOccurrencesTask = _persistenceProvider.UpdateCronJobOccurrencesWithUnifiedContext(
                cronJobIds,
                unifiedFunctionContext,
                cancellationToken
            );
            var updateTimeJobsTask = _persistenceProvider.UpdateTimeJobsWithUnifiedContext(
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
                await _persistenceProvider
                    .UpdateCronJobOccurrencesWithUnifiedContext(
                        cronJobIds,
                        unifiedFunctionContext,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (timeJobIds.Length != 0)
            {
                await _persistenceProvider
                    .UpdateTimeJobsWithUnifiedContext(timeJobIds, unifiedFunctionContext, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var resource in resources)
        {
            resource.Status = JobStatus.InProgress;

            if (resource.Type == JobType.TimeJob)
            {
                await _notificationHubSender
                    .UpdateTimeJobFromInternalFunctionContext<TTimeJob>(resource)
                    .ConfigureAwait(false);
            }
            else
            {
                await _notificationHubSender
                    .UpdateCronOccurrenceFromInternalFunctionContext<TCronJob>(resource)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task ReleaseAcquiredResources(
        InternalFunctionContext[] resources,
        CancellationToken cancellationToken = default
    )
    {
        if (resources is null)
        {
            await Task.WhenAll(
                _persistenceProvider.ReleaseAcquiredCronJobOccurrences([], cancellationToken),
                _persistenceProvider.ReleaseAcquiredTimeJobs([], cancellationToken)
            );
            return;
        }

        var cronJobIds =
            resources.Length == 0
                ? []
                : resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();

        if (cronJobIds.Length != 0)
        {
            await _persistenceProvider
                .ReleaseAcquiredCronJobOccurrences(cronJobIds, cancellationToken)
                .ConfigureAwait(false);
        }

        var timeJobIds =
            resources.Length == 0
                ? []
                : resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        if (timeJobIds.Length != 0)
        {
            await _persistenceProvider
                .ReleaseAcquiredTimeJobs(timeJobIds, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task UpdateTickerAsync(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        if (functionContext.Type == JobType.CronJobOccurrence)
        {
            await _persistenceProvider
                .UpdateCronJobOccurrence(functionContext, cancellationToken)
                .ConfigureAwait(false);
            await _notificationHubSender
                .UpdateCronOccurrenceFromInternalFunctionContext<TCronJob>(functionContext)
                .ConfigureAwait(false);
        }
        else
        {
            await _persistenceProvider.UpdateTimeJob(functionContext, cancellationToken).ConfigureAwait(false);
            await _notificationHubSender
                .UpdateTimeJobFromInternalFunctionContext<TTimeJob>(functionContext)
                .ConfigureAwait(false);
        }
    }

    public async Task UpdateSkipTimeJobsWithUnifiedContextAsync(
        InternalFunctionContext[] resources,
        CancellationToken cancellationToken = default
    )
    {
        var unifiedFunctionContext = new InternalFunctionContext { FunctionName = string.Empty }
            .SetProperty(x => x.Status, JobStatus.Skipped)
            .SetProperty(x => x.ExecutedAt, _timeProvider.GetUtcNow().UtcDateTime)
            .SetProperty(x => x.ExceptionDetails, "Rule RunCondition did not match!");

        if (resources.Length != 0)
        {
            await _persistenceProvider
                .UpdateTimeJobsWithUnifiedContext(
                    resources.Select(x => x.JobId).ToArray(),
                    unifiedFunctionContext,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var resource in resources)
        {
            resource.ExecutedAt = _timeProvider.GetUtcNow().UtcDateTime;
            resource.Status = JobStatus.Skipped;
            resource.ExceptionDetails = "Rule RunCondition did not match!";
            if (resource.Type == JobType.TimeJob)
            {
                await _notificationHubSender
                    .UpdateTimeJobFromInternalFunctionContext<TTimeJob>(resource)
                    .ConfigureAwait(false);
            }
            else
            {
                await _notificationHubSender
                    .UpdateCronOccurrenceFromInternalFunctionContext<TCronJob>(resource)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task<T?> GetRequestAsync<T>(
        Guid jobId,
        JobType type,
        CancellationToken cancellationToken = default
    )
    {
        var request =
            type == JobType.CronJobOccurrence
                ? await _persistenceProvider
                    .GetCronJobOccurrenceRequest(jobId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : await _persistenceProvider
                    .GetTimeJobRequest(jobId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

        return request == null ? default : JobsHelper.ReadJobRequest<T>(request);
    }

    public async Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
    {
        var results = new List<InternalFunctionContext>();

        await foreach (
            var timedOutTimeJob in _persistenceProvider
                .QueueTimedOutTimeJobs(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(
                new InternalFunctionContext
                {
                    FunctionName = timedOutTimeJob.Function,
                    JobId = timedOutTimeJob.Id,
                    Type = JobType.TimeJob,
                    Retries = timedOutTimeJob.Retries,
                    RetryIntervals = timedOutTimeJob.RetryIntervals,
                    ParentId = timedOutTimeJob.ParentId,
                    ExecutionTime = timedOutTimeJob.ExecutionTime ?? _timeProvider.GetUtcNow().UtcDateTime,
                    TimeJobChildren = timedOutTimeJob
                        .Children.Select(ch => new InternalFunctionContext
                        {
                            FunctionName = ch.Function,
                            JobId = ch.Id,
                            Type = JobType.TimeJob,
                            Retries = ch.Retries,
                            RetryIntervals = ch.RetryIntervals,
                            ParentId = ch.ParentId,
                            RunCondition = ch.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                            TimeJobChildren = ch
                                .Children.Select(gch => new InternalFunctionContext
                                {
                                    FunctionName = gch.Function,
                                    JobId = gch.Id,
                                    Type = JobType.TimeJob,
                                    Retries = gch.Retries,
                                    RetryIntervals = gch.RetryIntervals,
                                    ParentId = gch.ParentId,
                                    RunCondition = ch.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                                })
                                .ToList(),
                        })
                        .ToList(),
                }
            );

            await _notificationHubSender.UpdateTimeJobNotifyAsync(timedOutTimeJob).ConfigureAwait(false);
        }

        await foreach (
            var timedOutCronJob in _persistenceProvider
                .QueueTimedOutCronJobOccurrences(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            var functionContext = new InternalFunctionContext
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
            await _notificationHubSender
                .UpdateCronOccurrenceFromInternalFunctionContext<TCronJob>(functionContext)
                .ConfigureAwait(false);
        }

        return results.ToArray();
    }

    public async Task MigrateDefinedCronJobs(
        (string, string)[] cronExpressions,
        CancellationToken cancellationToken = default
    ) => await _persistenceProvider.MigrateDefinedCronJobs(cronExpressions, cancellationToken).ConfigureAwait(false);

    public async Task DeleteJob(Guid jobId, JobType type, CancellationToken cancellationToken = default)
    {
        if (type == JobType.CronJobOccurrence)
        {
            await _persistenceProvider.RemoveCronJobs([jobId], cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _persistenceProvider.RemoveTimeJobs([jobId], cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var cronOccurrence = _persistenceProvider.ReleaseDeadNodeOccurrenceResources(
            instanceIdentifier,
            cancellationToken
        );

        var timeJobs = _persistenceProvider.ReleaseDeadNodeTimeJobResources(
            instanceIdentifier,
            cancellationToken
        );

        await Task.WhenAll(cronOccurrence, timeJobs).ConfigureAwait(false);
    }
}
