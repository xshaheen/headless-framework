using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Headless.Jobs.Managers;

internal class JobsManager<TTimeJob, TCronJob>(
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    IJobsHostScheduler tickerQHostScheduler,
    TimeProvider timeProvider,
    IJobsNotificationHubSender notificationHubSender,
    JobsExecutionContext executionContext,
    IJobsDispatcher dispatcher
) : ICronJobManager<TCronJob>, ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IJobsHostScheduler _tickerQHostScheduler = Argument.IsNotNull(tickerQHostScheduler);
    private readonly IJobsDispatcher _dispatcher = Argument.IsNotNull(dispatcher);
    private readonly JobsExecutionContext _executionContext = Argument.IsNotNull(executionContext);

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.AddAsync(
        TCronJob entity,
        CancellationToken cancellationToken
    ) => _AddCronJobAsync(entity, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.AddAsync(
        TTimeJob entity,
        CancellationToken cancellationToken
    ) => _AddTimeJobAsync(entity, cancellationToken);

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.UpdateAsync(
        TCronJob cronJob,
        CancellationToken cancellationToken
    ) => _UpdateCronJobAsync(cronJob, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.UpdateAsync(
        TTimeJob timeJob,
        CancellationToken cancellationToken
    ) => _UpdateTimeJobAsync(timeJob, cancellationToken);

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _DeleteCronJobAsync(id, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _DeleteTimeJobAsync(id, cancellationToken);

    Task<JobResult<List<TTimeJob>>> ITimeJobManager<TTimeJob>.AddBatchAsync(
        List<TTimeJob> entities,
        CancellationToken cancellationToken
    ) => _AddTimeJobsBatchAsync(entities, cancellationToken);

    Task<JobResult<List<TTimeJob>>> ITimeJobManager<TTimeJob>.UpdateBatchAsync(
        List<TTimeJob> timeJobs,
        CancellationToken cancellationToken
    ) => _UpdateTimeJobsBatchAsync(timeJobs, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.DeleteBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken
    ) => _DeleteTimeJobsBatchAsync(ids, cancellationToken);

    Task<JobResult<List<TCronJob>>> ICronJobManager<TCronJob>.AddBatchAsync(
        List<TCronJob> entities,
        CancellationToken cancellationToken
    ) => _AddCronJobsBatchAsync(entities, cancellationToken);

    Task<JobResult<List<TCronJob>>> ICronJobManager<TCronJob>.UpdateBatchAsync(
        List<TCronJob> cronJobs,
        CancellationToken cancellationToken
    ) => _UpdateCronJobsBatchAsync(cronJobs, cancellationToken);

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.DeleteBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken
    ) => _DeleteCronJobsBatchAsync(ids, cancellationToken);

    private async Task<JobResult<TTimeJob>> _AddTimeJobAsync(TTimeJob entity, CancellationToken cancellationToken)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        if (JobFunctionProvider.JobFunctions.All(x => x.Key != entity.Function))
        {
            return new JobResult<TTimeJob>(
                new JobValidatorException($"Cannot find JobFunction with name {entity.Function}")
            );
        }

        entity.ExecutionTime =
            entity.ExecutionTime == null
                ? timeProvider.GetUtcNow().UtcDateTime
                : _ConvertToUtcIfNeeded(entity.ExecutionTime.Value);

        entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var executionTime = entity.ExecutionTime!.Value;

            // Persist first
            await persistenceProvider.AddTimeJobs([entity], cancellationToken: cancellationToken);

            // Only try to dispatch immediately if dispatcher is enabled (background services running)
            if (_dispatcher.IsEnabled && executionTime <= now.AddSeconds(1))
            {
                // Acquire and mark InProgress in one provider call
                var acquired = await persistenceProvider
                    .AcquireImmediateTimeJobsAsync([entity.Id], cancellationToken)
                    .ConfigureAwait(false);

                if (acquired.Length > 0)
                {
                    var contexts = _BuildImmediateContextsFromNonGeneric(acquired);
                    _CacheFunctionReferences(contexts.AsSpan());
                    await _dispatcher.DispatchAsync(contexts, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                _tickerQHostScheduler.RestartIfNeeded(executionTime);
            }

            await notificationHubSender.AddTimeJobNotifyAsync(entity.Id).ConfigureAwait(false);

            return new JobResult<TTimeJob>(entity);
        }
        catch (Exception e)
        {
            return new JobResult<TTimeJob>(e);
        }
    }

    private async Task<JobResult<TCronJob>> _AddCronJobAsync(TCronJob entity, CancellationToken cancellationToken)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        if (JobFunctionProvider.JobFunctions.All(x => x.Key != entity.Function))
        {
            return new JobResult<TCronJob>(
                new JobValidatorException($"Cannot find JobFunction with name {entity.Function}")
            );
        }

        if (
            CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, timeProvider.GetUtcNow().UtcDateTime)
            is not { } nextOccurrence
        )
        {
            return new JobResult<TCronJob>(new JobValidatorException($"Cannot parse expression {entity.Expression}"));
        }

        entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await persistenceProvider.InsertCronJobs([entity], cancellationToken: cancellationToken);

            _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);

            await notificationHubSender.AddCronJobNotifyAsync(entity);

            return new JobResult<TCronJob>(entity);
        }
        catch (Exception e)
        {
            return new JobResult<TCronJob>(e);
        }
    }

    private async Task<JobResult<TTimeJob>> _UpdateTimeJobAsync(TTimeJob timeJob, CancellationToken cancellationToken)
    {
        if (timeJob is null)
        {
            return new JobResult<TTimeJob>(new JobValidatorException($"Job must not be null!"));
        }

        if (timeJob.ExecutionTime == null)
        {
            return new JobResult<TTimeJob>(new JobValidatorException($"Job ExecutionTime must not be null!"));
        }

        timeJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        timeJob.ExecutionTime = _ConvertToUtcIfNeeded(timeJob.ExecutionTime.Value);

        try
        {
            var affectedRows = await persistenceProvider
                .UpdateTimeJobs([timeJob], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (_executionContext.Functions.Any(x => x.JobId == timeJob.Id))
            {
                _tickerQHostScheduler.Restart();
            }
            else
            {
                _tickerQHostScheduler.RestartIfNeeded(timeJob.ExecutionTime);
            }

            return new JobResult<TTimeJob>(timeJob, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<TTimeJob>(e);
        }
    }

    private async Task<JobResult<TCronJob>> _UpdateCronJobAsync(
        TCronJob cronJob,
        CancellationToken cancellationToken = default
    )
    {
        if (cronJob is null)
        {
            return new JobResult<TCronJob>(new ArgumentNullException(nameof(cronJob), @"Cron job must not be null!"));
        }

        if (JobFunctionProvider.JobFunctions.All(x => x.Key != cronJob.Function))
        {
            return new JobResult<TCronJob>(
                new JobValidatorException($"Cannot find JobFunction with name {cronJob.Function}")
            );
        }

        if (
            CronScheduleCache.GetNextOccurrenceOrDefault(cronJob.Expression, timeProvider.GetUtcNow().UtcDateTime)
            is not { } nextOccurrence
        )
        {
            return new JobResult<TCronJob>(new JobValidatorException($"Cannot parse expression {cronJob.Expression}"));
        }

        try
        {
            cronJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            var affectedRows = await persistenceProvider.UpdateCronJobs(
                [cronJob],
                cancellationToken: cancellationToken
            );

            if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronJob.Id) is { } internalFunction)
            {
                internalFunction.ResetUpdateProps().SetProperty(x => x.ExecutionTime, nextOccurrence);

                await persistenceProvider
                    .UpdateCronJobOccurrence(internalFunction, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _tickerQHostScheduler.Restart();
            }

            _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);

            return new JobResult<TCronJob>(cronJob, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<TCronJob>(e);
        }
    }

    private async Task<JobResult<TCronJob>> _DeleteCronJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var affectedRows = await persistenceProvider.RemoveCronJobs([id], cancellationToken: cancellationToken);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.ParentId == id))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TCronJob>(affectedRows);
    }

    private async Task<JobResult<TTimeJob>> _DeleteTimeJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var affectedRows = await persistenceProvider.RemoveTimeJobs([id], cancellationToken: cancellationToken);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.JobId == id))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TTimeJob>(affectedRows);
    }

    private static DateTime _ConvertToUtcIfNeeded(DateTime dateTime)
    {
        // If DateTime.Kind is Unspecified, assume it's in system timezone
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => TimeZoneInfo.ConvertTimeToUtc(dateTime, CronScheduleCache.TimeZoneInfo),
            _ => dateTime,
        };
    }

    // Batch operations implementation
    private static void _CacheFunctionReferences(Span<InternalFunctionContext> functions)
    {
        for (var i = 0; i < functions.Length; i++)
        {
            ref var context = ref functions[i];
            if (JobFunctionProvider.JobFunctions.TryGetValue(context.FunctionName, out var tickerItem))
            {
                context.CachedDelegate = tickerItem.Delegate;
                context.CachedPriority = tickerItem.Priority;
                context.CachedMaxConcurrency = tickerItem.MaxConcurrency;
            }

            if (context.TimeJobChildren is { Count: > 0 })
            {
                var childrenSpan = CollectionsMarshal.AsSpan(context.TimeJobChildren);
                _CacheFunctionReferences(childrenSpan);
            }
        }
    }

    private static InternalFunctionContext[] _BuildImmediateContextsFromNonGeneric(IEnumerable<TimeJobEntity> jobs)
    {
        return jobs.Select(_BuildContextFromNonGeneric).ToArray();
    }

    private static InternalFunctionContext _BuildContextFromNonGeneric(TimeJobEntity job)
    {
        return new InternalFunctionContext
        {
            FunctionName = job.Function,
            JobId = job.Id,
            Type = JobType.TimeJob,
            Retries = job.Retries,
            RetryIntervals = job.RetryIntervals,
            ParentId = job.ParentId,
            ExecutionTime = job.ExecutionTime ?? DateTime.UtcNow,
            RunCondition = job.RunCondition ?? RunCondition.OnAnyCompletedStatus,
            TimeJobChildren = job.Children.Select(_BuildContextFromNonGeneric).ToList(),
        };
    }

    private async Task<JobResult<List<TTimeJob>>> _AddTimeJobsBatchAsync(
        List<TTimeJob> entities,
        CancellationToken cancellationToken = default
    )
    {
        if (entities == null || entities.Count == 0)
        {
            return new JobResult<List<TTimeJob>>(entities ?? new List<TTimeJob>());
        }

        var jobFunctionsHashSet = new HashSet<string>(JobFunctionProvider.JobFunctions.Keys, StringComparer.Ordinal);
        var immediateTickers = new List<Guid>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime earliestForNonImmediate = default;
        foreach (var entity in entities)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            if (!jobFunctionsHashSet.Contains(entity.Function))
            {
                return new JobResult<List<TTimeJob>>(
                    new JobValidatorException($"Cannot find JobFunction with name {entity.Function}")
                );
            }

            entity.ExecutionTime ??= now;
            entity.ExecutionTime = _ConvertToUtcIfNeeded(entity.ExecutionTime.Value);

            // Align with single AddTimeJobAsync: initialize timestamps
            entity.CreatedAt = now;
            entity.UpdatedAt = now;

            if (entity.ExecutionTime.Value <= now.AddSeconds(1))
            {
                immediateTickers.Add(entity.Id);
            }
            else if (earliestForNonImmediate == default || entity.ExecutionTime <= earliestForNonImmediate)
            {
                earliestForNonImmediate = entity.ExecutionTime.Value;
            }
        }

        try
        {
            await persistenceProvider.AddTimeJobs(entities.ToArray(), cancellationToken: cancellationToken);

            await notificationHubSender.AddTimeJobsBatchNotifyAsync().ConfigureAwait(false);

            // Only try to dispatch immediately if dispatcher is enabled (background services running)
            if (_dispatcher.IsEnabled && immediateTickers.Count > 0)
            {
                var acquired = await persistenceProvider
                    .AcquireImmediateTimeJobsAsync(immediateTickers.ToArray(), cancellationToken)
                    .ConfigureAwait(false);

                if (acquired.Length > 0)
                {
                    var contexts = _BuildImmediateContextsFromNonGeneric(acquired);
                    _CacheFunctionReferences(contexts.AsSpan());
                    await _dispatcher.DispatchAsync(contexts, cancellationToken).ConfigureAwait(false);
                }
            }

            if (earliestForNonImmediate != default)
            {
                _tickerQHostScheduler.RestartIfNeeded(earliestForNonImmediate);
            }

            return new JobResult<List<TTimeJob>>(entities);
        }
        catch (Exception e)
        {
            return new JobResult<List<TTimeJob>>(e);
        }
    }

    private async Task<JobResult<List<TCronJob>>> _AddCronJobsBatchAsync(
        List<TCronJob> entities,
        CancellationToken cancellationToken = default
    )
    {
        var validEntities = new List<TCronJob>();
        var errors = new List<Exception>();
        var nextOccurrences = new List<DateTime>();

        foreach (var entity in entities)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            if (JobFunctionProvider.JobFunctions.All(x => x.Key != entity.Function))
            {
                errors.Add(new JobValidatorException($"Cannot find JobFunction with name {entity.Function}"));
                continue;
            }

            if (
                CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, timeProvider.GetUtcNow().UtcDateTime)
                is not { } nextOccurrence
            )
            {
                errors.Add(new JobValidatorException($"Cannot parse expression {entity.Expression}"));
                continue;
            }

            entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
            entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            validEntities.Add(entity);
            nextOccurrences.Add(nextOccurrence);
        }

        if (errors.Count != 0)
        {
            return new JobResult<List<TCronJob>>(errors.First());
        }

        try
        {
            await persistenceProvider.InsertCronJobs(validEntities.ToArray(), cancellationToken: cancellationToken);

            if (validEntities.Count != 0)
            {
                // Restart scheduler for earliest occurrence
                var earliestOccurrence = nextOccurrences.Min();
                _tickerQHostScheduler.RestartIfNeeded(earliestOccurrence);

                // Send notifications for all
                foreach (var entity in validEntities)
                {
                    await notificationHubSender.AddCronJobNotifyAsync(entity);
                }
            }

            return new JobResult<List<TCronJob>>(validEntities);
        }
        catch (Exception e)
        {
            return new JobResult<List<TCronJob>>(e);
        }
    }

    private async Task<JobResult<List<TTimeJob>>> _UpdateTimeJobsBatchAsync(
        List<TTimeJob> timeJobs,
        CancellationToken cancellationToken = default
    )
    {
        var validTickers = new List<TTimeJob>();
        var errors = new List<Exception>();
        var needsRestart = false;

        foreach (var timeJob in timeJobs)
        {
            if (timeJob is null)
            {
                errors.Add(new JobValidatorException("Job must not be null!"));
                continue;
            }

            if (timeJob.ExecutionTime == null)
            {
                errors.Add(new JobValidatorException("Job ExecutionTime must not be null!"));
                continue;
            }

            timeJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            timeJob.ExecutionTime = _ConvertToUtcIfNeeded(timeJob.ExecutionTime.Value);

            if (_executionContext.Functions.Any(x => x.JobId == timeJob.Id))
            {
                needsRestart = true;
            }

            validTickers.Add(timeJob);
        }

        if (errors.Count != 0)
        {
            return new JobResult<List<TTimeJob>>(errors.First());
        }

        try
        {
            var affectedRows = await persistenceProvider
                .UpdateTimeJobs(validTickers.ToArray(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (needsRestart)
            {
                _tickerQHostScheduler.Restart();
            }
            else if (validTickers.Count != 0)
            {
                var earliestExecution = validTickers.Min(t => t.ExecutionTime);
                _tickerQHostScheduler.RestartIfNeeded(earliestExecution);
            }

            return new JobResult<List<TTimeJob>>(validTickers, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<List<TTimeJob>>(e);
        }
    }

    private async Task<JobResult<List<TCronJob>>> _UpdateCronJobsBatchAsync(
        List<TCronJob> cronJobs,
        CancellationToken cancellationToken = default
    )
    {
        var validTickers = new List<TCronJob>();
        var errors = new List<Exception>();
        var nextOccurrences = new List<DateTime>();
        var needsRestart = false;
        var internalFunctionsToUpdate = new List<InternalFunctionContext>();

        foreach (var cronJob in cronJobs)
        {
            if (cronJob is null)
            {
                errors.Add(new ArgumentNullException(nameof(cronJobs), @"Cron job must not be null!"));
                continue;
            }

            if (JobFunctionProvider.JobFunctions.All(x => x.Key != cronJob.Function))
            {
                errors.Add(new JobValidatorException($"Cannot find JobFunction with name {cronJob.Function}"));
                continue;
            }

            if (
                CronScheduleCache.GetNextOccurrenceOrDefault(cronJob.Expression, timeProvider.GetUtcNow().UtcDateTime)
                is not { } nextOccurrence
            )
            {
                errors.Add(new JobValidatorException($"Cannot parse expression {cronJob.Expression}"));
                continue;
            }

            cronJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronJob.Id) is { } internalFunction)
            {
                internalFunction.ResetUpdateProps().SetProperty(x => x.ExecutionTime, nextOccurrence);
                internalFunctionsToUpdate.Add(internalFunction);
                needsRestart = true;
            }

            validTickers.Add(cronJob);
            nextOccurrences.Add(nextOccurrence);
        }

        if (errors.Count != 0)
        {
            return new JobResult<List<TCronJob>>(errors.First());
        }

        try
        {
            var affectedRows = await persistenceProvider.UpdateCronJobs(
                validTickers.ToArray(),
                cancellationToken: cancellationToken
            );

            // Update internal functions for those that need it
            foreach (var internalFunction in internalFunctionsToUpdate)
            {
                await persistenceProvider
                    .UpdateCronJobOccurrence(internalFunction, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (needsRestart)
            {
                _tickerQHostScheduler.Restart();
            }
            else if (nextOccurrences.Count != 0)
            {
                var earliestOccurrence = nextOccurrences.Min();
                _tickerQHostScheduler.RestartIfNeeded(earliestOccurrence);
            }

            return new JobResult<List<TCronJob>>(validTickers, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<List<TCronJob>>(e);
        }
    }

    private async Task<JobResult<TTimeJob>> _DeleteTimeJobsBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await persistenceProvider.RemoveTimeJobs(
            ids.ToArray(),
            cancellationToken: cancellationToken
        );

        if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.JobId)))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TTimeJob>(affectedRows);
    }

    private async Task<JobResult<TCronJob>> _DeleteCronJobsBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await persistenceProvider.RemoveCronJobs(
            ids.ToArray(),
            cancellationToken: cancellationToken
        );

        if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.ParentId ?? Guid.Empty)))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TCronJob>(affectedRows);
    }
}
