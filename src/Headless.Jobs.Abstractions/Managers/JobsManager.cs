using System.Runtime.InteropServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Headless.Jobs.Managers;

internal class JobsManager<TTimeTicker, TCronTicker>
    : ICronJobManager<TCronTicker>,
        ITimeJobManager<TTimeTicker>
    where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
    where TCronTicker : CronJobEntity, new()
{
    private readonly IJobPersistenceProvider<TTimeTicker, TCronTicker> _persistenceProvider;
    private readonly IJobsHostScheduler _tickerQHostScheduler;
    private readonly IJobClock _clock;
    private readonly IJobsNotificationHubSender _notificationHubSender;
    private readonly IJobsDispatcher _dispatcher;
    private readonly JobsExecutionContext _executionContext;

    public JobsManager(
        IJobPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
        IJobsHostScheduler tickerQHostScheduler,
        IJobClock clock,
        IJobsNotificationHubSender notificationHubSender,
        JobsExecutionContext executionContext,
        IJobsDispatcher dispatcher
    )
    {
        _persistenceProvider = persistenceProvider;
        _tickerQHostScheduler = tickerQHostScheduler ?? throw new ArgumentNullException(nameof(tickerQHostScheduler));
        _clock = clock;
        _notificationHubSender = notificationHubSender;
        _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    Task<JobResult<TCronTicker>> ICronJobManager<TCronTicker>.AddAsync(
        TCronTicker entity,
        CancellationToken cancellationToken
    ) => _AddCronTickerAsync(entity, cancellationToken);

    Task<JobResult<TTimeTicker>> ITimeJobManager<TTimeTicker>.AddAsync(
        TTimeTicker entity,
        CancellationToken cancellationToken
    ) => _AddTimeTickerAsync(entity, cancellationToken);

    Task<JobResult<TCronTicker>> ICronJobManager<TCronTicker>.UpdateAsync(
        TCronTicker cronTicker,
        CancellationToken cancellationToken
    ) => _UpdateCronTickerAsync(cronTicker, cancellationToken);

    Task<JobResult<TTimeTicker>> ITimeJobManager<TTimeTicker>.UpdateAsync(
        TTimeTicker timeTicker,
        CancellationToken cancellationToken
    ) => _UpdateTimeTickerAsync(timeTicker, cancellationToken);

    Task<JobResult<TCronTicker>> ICronJobManager<TCronTicker>.DeleteAsync(
        Guid id,
        CancellationToken cancellationToken
    ) => _DeleteCronTickerAsync(id, cancellationToken);

    Task<JobResult<TTimeTicker>> ITimeJobManager<TTimeTicker>.DeleteAsync(
        Guid id,
        CancellationToken cancellationToken
    ) => _DeleteTimeTickerAsync(id, cancellationToken);

    Task<JobResult<List<TTimeTicker>>> ITimeJobManager<TTimeTicker>.AddBatchAsync(
        List<TTimeTicker> entities,
        CancellationToken cancellationToken
    ) => _AddTimeTickersBatchAsync(entities, cancellationToken);

    Task<JobResult<List<TTimeTicker>>> ITimeJobManager<TTimeTicker>.UpdateBatchAsync(
        List<TTimeTicker> timeTickers,
        CancellationToken cancellationToken
    ) => _UpdateTimeTickersBatchAsync(timeTickers, cancellationToken);

    Task<JobResult<TTimeTicker>> ITimeJobManager<TTimeTicker>.DeleteBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken
    ) => _DeleteTimeTickersBatchAsync(ids, cancellationToken);

    Task<JobResult<List<TCronTicker>>> ICronJobManager<TCronTicker>.AddBatchAsync(
        List<TCronTicker> entities,
        CancellationToken cancellationToken
    ) => _AddCronTickersBatchAsync(entities, cancellationToken);

    Task<JobResult<List<TCronTicker>>> ICronJobManager<TCronTicker>.UpdateBatchAsync(
        List<TCronTicker> cronTickers,
        CancellationToken cancellationToken
    ) => _UpdateCronTickersBatchAsync(cronTickers, cancellationToken);

    Task<JobResult<TCronTicker>> ICronJobManager<TCronTicker>.DeleteBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken
    ) => _DeleteCronTickersBatchAsync(ids, cancellationToken);

    private async Task<JobResult<TTimeTicker>> _AddTimeTickerAsync(
        TTimeTicker entity,
        CancellationToken cancellationToken
    )
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        if (JobFunctionProvider.JobFunctions.All(x => x.Key != entity?.Function))
        {
            return new JobResult<TTimeTicker>(
                new JobValidatorException($"Cannot find JobFunction with name {entity?.Function}")
            );
        }

        entity.ExecutionTime =
            entity.ExecutionTime == null ? _clock.UtcNow : _ConvertToUtcIfNeeded(entity.ExecutionTime.Value);

        entity.CreatedAt = _clock.UtcNow;
        entity.UpdatedAt = _clock.UtcNow;

        try
        {
            var now = _clock.UtcNow;
            var executionTime = entity.ExecutionTime!.Value;

            // Persist first
            await _persistenceProvider.AddTimeTickers([entity], cancellationToken: cancellationToken);

            // Only try to dispatch immediately if dispatcher is enabled (background services running)
            if (_dispatcher.IsEnabled && executionTime <= now.AddSeconds(1))
            {
                // Acquire and mark InProgress in one provider call
                var acquired = await _persistenceProvider
                    .AcquireImmediateTimeTickersAsync([entity.Id], cancellationToken)
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

            await _notificationHubSender.AddTimeJobNotifyAsync(entity.Id).ConfigureAwait(false);

            return new JobResult<TTimeTicker>(entity);
        }
        catch (Exception e)
        {
            return new JobResult<TTimeTicker>(e);
        }
    }

    private async Task<JobResult<TCronTicker>> _AddCronTickerAsync(
        TCronTicker entity,
        CancellationToken cancellationToken
    )
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        if (JobFunctionProvider.JobFunctions.All(x => x.Key != entity?.Function))
        {
            return new JobResult<TCronTicker>(
                new JobValidatorException($"Cannot find JobFunction with name {entity?.Function}")
            );
        }

        if (CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, _clock.UtcNow) is not { } nextOccurrence)
        {
            return new JobResult<TCronTicker>(
                new JobValidatorException($"Cannot parse expression {entity.Expression}")
            );
        }

        entity.CreatedAt = _clock.UtcNow;
        entity.UpdatedAt = _clock.UtcNow;

        try
        {
            await _persistenceProvider.InsertCronTickers([entity], cancellationToken: cancellationToken);

            _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);

            await _notificationHubSender.AddCronJobNotifyAsync(entity);

            return new JobResult<TCronTicker>(entity);
        }
        catch (Exception e)
        {
            return new JobResult<TCronTicker>(e);
        }
    }

    private async Task<JobResult<TTimeTicker>> _UpdateTimeTickerAsync(
        TTimeTicker timeTicker,
        CancellationToken cancellationToken
    )
    {
        if (timeTicker is null)
        {
            return new JobResult<TTimeTicker>(new JobValidatorException($"Ticker must not be null!"));
        }

        if (timeTicker.ExecutionTime == null)
        {
            return new JobResult<TTimeTicker>(
                new JobValidatorException($"Ticker ExecutionTime must not be null!")
            );
        }

        timeTicker.UpdatedAt = _clock.UtcNow;
        timeTicker.ExecutionTime = _ConvertToUtcIfNeeded(timeTicker.ExecutionTime.Value);

        try
        {
            var affectedRows = await _persistenceProvider
                .UpdateTimeTickers([timeTicker], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (_executionContext.Functions.Any(x => x.JobId == timeTicker.Id))
            {
                _tickerQHostScheduler.Restart();
            }
            else
            {
                _tickerQHostScheduler.RestartIfNeeded(timeTicker.ExecutionTime);
            }

            return new JobResult<TTimeTicker>(timeTicker, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<TTimeTicker>(e);
        }
    }

    private async Task<JobResult<TCronTicker>> _UpdateCronTickerAsync(
        TCronTicker cronTicker,
        CancellationToken cancellationToken = default
    )
    {
        if (cronTicker is null)
        {
            return new JobResult<TCronTicker>(
                new ArgumentNullException(nameof(cronTicker), "Cron ticker must not be null!")
            );
        }

        if (JobFunctionProvider.JobFunctions.All(x => x.Key != cronTicker?.Function))
        {
            return new JobResult<TCronTicker>(
                new JobValidatorException($"Cannot find JobFunction with name {cronTicker.Function}")
            );
        }

        if (
            CronScheduleCache.GetNextOccurrenceOrDefault(cronTicker.Expression, _clock.UtcNow) is not { } nextOccurrence
        )
        {
            return new JobResult<TCronTicker>(
                new JobValidatorException($"Cannot parse expression {cronTicker.Expression}")
            );
        }

        try
        {
            cronTicker.UpdatedAt = _clock.UtcNow;

            var affectedRows = await _persistenceProvider.UpdateCronTickers(
                [cronTicker],
                cancellationToken: cancellationToken
            );

            if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronTicker.Id) is { } internalFunction)
            {
                internalFunction.ResetUpdateProps().SetProperty(x => x.ExecutionTime, nextOccurrence);

                await _persistenceProvider
                    .UpdateCronTickerOccurrence(internalFunction, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _tickerQHostScheduler.Restart();
            }

            _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);

            return new JobResult<TCronTicker>(cronTicker, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<TCronTicker>(e);
        }
    }

    private async Task<JobResult<TCronTicker>> _DeleteCronTickerAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await _persistenceProvider.RemoveCronTickers([id], cancellationToken: cancellationToken);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.ParentId == id))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TCronTicker>(affectedRows);
    }

    private async Task<JobResult<TTimeTicker>> _DeleteTimeTickerAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await _persistenceProvider.RemoveTimeTickers([id], cancellationToken: cancellationToken);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.JobId == id))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TTimeTicker>(affectedRows);
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

    private static InternalFunctionContext[] _BuildImmediateContextsFromNonGeneric(
        IEnumerable<TimeJobEntity> tickers
    )
    {
        return tickers.Select(_BuildContextFromNonGeneric).ToArray();
    }

    private static InternalFunctionContext _BuildContextFromNonGeneric(TimeJobEntity ticker)
    {
        return new InternalFunctionContext
        {
            FunctionName = ticker.Function,
            JobId = ticker.Id,
            Type = JobType.TimeJob,
            Retries = ticker.Retries,
            RetryIntervals = ticker.RetryIntervals,
            ParentId = ticker.ParentId,
            ExecutionTime = ticker.ExecutionTime ?? DateTime.UtcNow,
            RunCondition = ticker.RunCondition ?? RunCondition.OnAnyCompletedStatus,
            TimeJobChildren = ticker.Children.Select(_BuildContextFromNonGeneric).ToList(),
        };
    }

    private async Task<JobResult<List<TTimeTicker>>> _AddTimeTickersBatchAsync(
        List<TTimeTicker> entities,
        CancellationToken cancellationToken = default
    )
    {
        if (entities == null || entities.Count == 0)
        {
            return new JobResult<List<TTimeTicker>>(entities ?? new List<TTimeTicker>());
        }

        var jobFunctionsHashSet = new HashSet<string>(
            JobFunctionProvider.JobFunctions.Keys,
            StringComparer.Ordinal
        );
        var immediateTickers = new List<Guid>();
        var now = _clock.UtcNow;
        DateTime earliestForNonImmediate = default;
        foreach (var entity in entities)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            if (!jobFunctionsHashSet.Contains(entity.Function))
            {
                return new JobResult<List<TTimeTicker>>(
                    new JobValidatorException($"Cannot find JobFunction with name {entity?.Function}")
                );
            }

            entity.ExecutionTime ??= now;
            entity.ExecutionTime = _ConvertToUtcIfNeeded(entity.ExecutionTime.Value);

            // Align with single AddTimeTickerAsync: initialize timestamps
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
            await _persistenceProvider.AddTimeTickers(entities.ToArray(), cancellationToken: cancellationToken);

            await _notificationHubSender.AddTimeJobsBatchNotifyAsync().ConfigureAwait(false);

            // Only try to dispatch immediately if dispatcher is enabled (background services running)
            if (_dispatcher.IsEnabled && immediateTickers.Count > 0)
            {
                var acquired = await _persistenceProvider
                    .AcquireImmediateTimeTickersAsync(immediateTickers.ToArray(), cancellationToken)
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

            return new JobResult<List<TTimeTicker>>(entities);
        }
        catch (Exception e)
        {
            return new JobResult<List<TTimeTicker>>(e);
        }
    }

    private async Task<JobResult<List<TCronTicker>>> _AddCronTickersBatchAsync(
        List<TCronTicker> entities,
        CancellationToken cancellationToken = default
    )
    {
        var validEntities = new List<TCronTicker>();
        var errors = new List<Exception>();
        var nextOccurrences = new List<DateTime>();

        foreach (var entity in entities)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            if (JobFunctionProvider.JobFunctions.All(x => x.Key != entity?.Function))
            {
                errors.Add(new JobValidatorException($"Cannot find JobFunction with name {entity?.Function}"));
                continue;
            }

            if (
                CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, _clock.UtcNow) is not { } nextOccurrence
            )
            {
                errors.Add(new JobValidatorException($"Cannot parse expression {entity.Expression}"));
                continue;
            }

            entity.CreatedAt = _clock.UtcNow;
            entity.UpdatedAt = _clock.UtcNow;

            validEntities.Add(entity);
            nextOccurrences.Add(nextOccurrence);
        }

        if (errors.Count != 0)
        {
            return new JobResult<List<TCronTicker>>(errors.First());
        }

        try
        {
            await _persistenceProvider.InsertCronTickers(validEntities.ToArray(), cancellationToken: cancellationToken);

            if (validEntities.Count != 0)
            {
                // Restart scheduler for earliest occurrence
                var earliestOccurrence = nextOccurrences.Min();
                _tickerQHostScheduler.RestartIfNeeded(earliestOccurrence);

                // Send notifications for all
                foreach (var entity in validEntities)
                {
                    await _notificationHubSender.AddCronJobNotifyAsync(entity);
                }
            }

            return new JobResult<List<TCronTicker>>(validEntities);
        }
        catch (Exception e)
        {
            return new JobResult<List<TCronTicker>>(e);
        }
    }

    private async Task<JobResult<List<TTimeTicker>>> _UpdateTimeTickersBatchAsync(
        List<TTimeTicker> timeTickers,
        CancellationToken cancellationToken = default
    )
    {
        var validTickers = new List<TTimeTicker>();
        var errors = new List<Exception>();
        var needsRestart = false;

        foreach (var timeTicker in timeTickers)
        {
            if (timeTicker is null)
            {
                errors.Add(new JobValidatorException("Ticker must not be null!"));
                continue;
            }

            if (timeTicker.ExecutionTime == null)
            {
                errors.Add(new JobValidatorException("Ticker ExecutionTime must not be null!"));
                continue;
            }

            timeTicker.UpdatedAt = _clock.UtcNow;
            timeTicker.ExecutionTime = _ConvertToUtcIfNeeded(timeTicker.ExecutionTime.Value);

            if (_executionContext.Functions.Any(x => x.JobId == timeTicker.Id))
            {
                needsRestart = true;
            }

            validTickers.Add(timeTicker);
        }

        if (errors.Count != 0)
        {
            return new JobResult<List<TTimeTicker>>(errors.First());
        }

        try
        {
            var affectedRows = await _persistenceProvider
                .UpdateTimeTickers(validTickers.ToArray(), cancellationToken: cancellationToken)
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

            return new JobResult<List<TTimeTicker>>(validTickers, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<List<TTimeTicker>>(e);
        }
    }

    private async Task<JobResult<List<TCronTicker>>> _UpdateCronTickersBatchAsync(
        List<TCronTicker> cronTickers,
        CancellationToken cancellationToken = default
    )
    {
        var validTickers = new List<TCronTicker>();
        var errors = new List<Exception>();
        var nextOccurrences = new List<DateTime>();
        var needsRestart = false;
        var internalFunctionsToUpdate = new List<InternalFunctionContext>();

        foreach (var cronTicker in cronTickers)
        {
            if (cronTicker is null)
            {
                errors.Add(new ArgumentNullException(nameof(cronTickers), "Cron ticker must not be null!"));
                continue;
            }

            if (JobFunctionProvider.JobFunctions.All(x => x.Key != cronTicker?.Function))
            {
                errors.Add(new JobValidatorException($"Cannot find JobFunction with name {cronTicker.Function}"));
                continue;
            }

            if (
                CronScheduleCache.GetNextOccurrenceOrDefault(cronTicker.Expression, _clock.UtcNow)
                is not { } nextOccurrence
            )
            {
                errors.Add(new JobValidatorException($"Cannot parse expression {cronTicker.Expression}"));
                continue;
            }

            cronTicker.UpdatedAt = _clock.UtcNow;

            if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronTicker.Id) is { } internalFunction)
            {
                internalFunction.ResetUpdateProps().SetProperty(x => x.ExecutionTime, nextOccurrence);
                internalFunctionsToUpdate.Add(internalFunction);
                needsRestart = true;
            }

            validTickers.Add(cronTicker);
            nextOccurrences.Add(nextOccurrence);
        }

        if (errors.Count != 0)
        {
            return new JobResult<List<TCronTicker>>(errors.First());
        }

        try
        {
            var affectedRows = await _persistenceProvider.UpdateCronTickers(
                validTickers.ToArray(),
                cancellationToken: cancellationToken
            );

            // Update internal functions for those that need it
            foreach (var internalFunction in internalFunctionsToUpdate)
            {
                await _persistenceProvider
                    .UpdateCronTickerOccurrence(internalFunction, cancellationToken: cancellationToken)
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

            return new JobResult<List<TCronTicker>>(validTickers, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<List<TCronTicker>>(e);
        }
    }

    private async Task<JobResult<TTimeTicker>> _DeleteTimeTickersBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await _persistenceProvider.RemoveTimeTickers(
            ids.ToArray(),
            cancellationToken: cancellationToken
        );

        if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.JobId)))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TTimeTicker>(affectedRows);
    }

    private async Task<JobResult<TCronTicker>> _DeleteCronTickersBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await _persistenceProvider.RemoveCronTickers(
            ids.ToArray(),
            cancellationToken: cancellationToken
        );

        if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.ParentId ?? Guid.Empty)))
        {
            _tickerQHostScheduler.Restart();
        }

        return new JobResult<TCronTicker>(affectedRows);
    }
}
