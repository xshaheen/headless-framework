// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Abstractions;
using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Managers;

internal partial class JobsManager<TTimeJob, TCronJob>(
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    IJobsHostScheduler jobsHostScheduler,
    TimeProvider timeProvider,
    IGuidGenerator guidGenerator,
    IJobsNotificationHubSender notificationHubSender,
    JobsExecutionContext executionContext,
    IJobsDispatcher dispatcher,
    ICurrentCommitCoordinator currentCommitCoordinator,
    CronScheduleCache cronScheduleCache,
    SchedulerOptionsBuilder schedulerOptions,
    ILogger<JobsManager<TTimeJob, TCronJob>> logger,
    IServiceScopeFactory? serviceScopeFactory = null
) : ICronJobManager<TCronJob>, ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IJobsHostScheduler _jobsHostScheduler = Argument.IsNotNull(jobsHostScheduler);
    private readonly IJobsDispatcher _dispatcher = Argument.IsNotNull(dispatcher);
    private readonly JobsExecutionContext _executionContext = Argument.IsNotNull(executionContext);
    private readonly ICurrentCommitCoordinator _currentCommitCoordinator = Argument.IsNotNull(currentCommitCoordinator);
    private readonly CronScheduleCache _cronScheduleCache = Argument.IsNotNull(cronScheduleCache);
    private readonly TimeSpan _postCommitDrainTimeout = Argument.IsNotNull(schedulerOptions).PostCommitDrainTimeout;
    private readonly ILogger<JobsManager<TTimeJob, TCronJob>> _logger = Argument.IsNotNull(logger);
    private readonly IServiceScopeFactory? _serviceScopeFactory = serviceScopeFactory;

    // Add is the transaction-enlisting op: it returns the persisted entity and THROWS on any failure — validation
    // (JobValidatorException), a dead/completed coordinated transaction or a mis-wired provider (InvalidOperationException),
    // and persistence faults all propagate. On the coordinated path a propagated failure is the point: it lets the
    // caller's ambient transaction roll back rather than commit without the job row. Update/Delete are plain CRUD and
    // keep returning JobResult.
    Task<TCronJob> ICronJobManager<TCronJob>.AddAsync(TCronJob entity, CancellationToken cancellationToken) =>
        _AddCronJobAsync(entity, cancellationToken);

    // See the throw-on-failure note on ICronJobManager.AddAsync above — the same applies to the time-job Add path.
    Task<TTimeJob> ITimeJobManager<TTimeJob>.AddAsync(TTimeJob entity, CancellationToken cancellationToken) =>
        _AddTimeJobAsync(entity, cancellationToken);

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

    Task<List<TTimeJob>> ITimeJobManager<TTimeJob>.AddBatchAsync(
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

    Task<List<TCronJob>> ICronJobManager<TCronJob>.AddBatchAsync(
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

    private async Task<TTimeJob> _AddTimeJobAsync(TTimeJob entity, CancellationToken cancellationToken)
    {
        await _RunSchedulePipelineAsync(entity, cancellationToken).ConfigureAwait(false);

        if (entity.Id == Guid.Empty)
        {
            entity.Id = guidGenerator.Create();
        }

        if (JobFunctionProvider.JobFunctions.All(x => !string.Equals(x.Key, entity.Function, StringComparison.Ordinal)))
        {
            throw new JobValidatorException($"Cannot find JobFunction with name {entity.Function}");
        }

        entity.ExecutionTime =
            entity.ExecutionTime == null
                ? timeProvider.GetUtcNow().UtcDateTime
                : _ConvertToUtcIfNeeded(entity.ExecutionTime.Value);

        entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Synchronous capture before the first await (KTD-1): a dead/completed coordinated transaction or a mis-wired
        // provider throws here and propagates (KTD-2). Add never swallows a failure into a result — the write and any
        // persistence fault propagate too, so on the coordinated path the caller's transaction rolls back rather than
        // committing without the job row, the exact divergence this feature prevents.
        var coordinated = _TryCaptureCoordinatedContext();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var executionTime = entity.ExecutionTime.Value;

        if (coordinated is { } context)
        {
            // Write the row inside the caller's transaction; defer dispatch/scheduler/notify to commit (KTD-4). A
            // returned entity means the row was enlisted into the transaction (it commits with it), not that the
            // deferred dispatch ran — a post-commit dispatch failure is recovered by the scheduler's polling sweep.
            await context
                .Writer.WriteTimeJobsAsync([entity], context.Relational, cancellationToken)
                .ConfigureAwait(false);

            // Re-read the clock at commit time: the deferred lambda runs when the caller's transaction commits, which
            // can be much later than enqueue. Using the enqueue-time `now` could push a job that was within the
            // immediate-dispatch window into the scheduler/poll-sweep path. (Direct path below stays in-band, so its
            // `now` is already current.)
            _DeferSideEffects(
                context.Coordinator,
                entity.Id.ToString(),
                ct => _RunTimeJobSideEffectsAsync(entity, timeProvider.GetUtcNow().UtcDateTime, executionTime, ct)
            );

            return entity;
        }

        // Direct path (no coordinator / non-relational scope): persist then run side effects in-band.
        await persistenceProvider
            .AddTimeJobsAsync([entity], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _RunTimeJobSideEffectsAsync(entity, now, executionTime, cancellationToken).ConfigureAwait(false);

        return entity;
    }

    // Side effects for a single time-job enqueue: immediate dispatch (when due) or scheduler restart, then notify.
    // Runs in-band on the direct path and deferred via OnCommit on the coordinated path. On the coordinated path the
    // row is already committed when this runs, so AcquireImmediateTimeJobsAsync (its own connection) observes it.
    private async Task _RunTimeJobSideEffectsAsync(
        TTimeJob entity,
        DateTime now,
        DateTime executionTime,
        CancellationToken cancellationToken
    )
    {
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
            _jobsHostScheduler.RestartIfNeeded(executionTime);
        }

        await notificationHubSender.AddTimeJobNotifyAsync(entity.Id).ConfigureAwait(false);
    }

    private async Task<TCronJob> _AddCronJobAsync(TCronJob entity, CancellationToken cancellationToken)
    {
        await _RunSchedulePipelineAsync(entity, cancellationToken).ConfigureAwait(false);

        if (entity.Id == Guid.Empty)
        {
            entity.Id = guidGenerator.Create();
        }

        if (JobFunctionProvider.JobFunctions.All(x => !string.Equals(x.Key, entity.Function, StringComparison.Ordinal)))
        {
            throw new JobValidatorException($"Cannot find JobFunction with name {entity.Function}");
        }

        if (
            _cronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, timeProvider.GetUtcNow().UtcDateTime)
            is not { } nextOccurrence
        )
        {
            throw new JobValidatorException($"Cannot parse expression {entity.Expression}");
        }

        entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Synchronous capture before the first await; dead-transaction / mis-wire / write faults propagate (KTD-2).
        var coordinated = _TryCaptureCoordinatedContext();

        if (coordinated is { } context)
        {
            await context
                .Writer.WriteCronJobsAsync([entity], context.Relational, cancellationToken)
                .ConfigureAwait(false);

            // Cron has no immediate-dispatch branch; defer cache-invalidation + scheduler-restart + notify (KTD-4).
            _DeferSideEffects(
                context.Coordinator,
                entity.Id.ToString(),
                ct => _RunCoordinatedCronJobSideEffectsAsync(context.Writer, entity, nextOccurrence, ct)
            );

            return entity;
        }

        await persistenceProvider
            .InsertCronJobsAsync([entity], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _jobsHostScheduler.RestartIfNeeded(nextOccurrence);

        await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);

        return entity;
    }

    private async Task<JobResult<TTimeJob>> _UpdateTimeJobAsync(TTimeJob timeJob, CancellationToken cancellationToken)
    {
        if (timeJob is null)
        {
            return new JobResult<TTimeJob>(new JobValidatorException("Job must not be null!"));
        }

        if (timeJob.ExecutionTime == null)
        {
            return new JobResult<TTimeJob>(new JobValidatorException("Job ExecutionTime must not be null!"));
        }

        timeJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        timeJob.ExecutionTime = _ConvertToUtcIfNeeded(timeJob.ExecutionTime.Value);

        try
        {
            var affectedRows = await persistenceProvider
                .UpdateTimeJobsAsync([timeJob], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (_executionContext.Functions.Any(x => x.JobId == timeJob.Id))
            {
                _jobsHostScheduler.Restart();
            }
            else
            {
                _jobsHostScheduler.RestartIfNeeded(timeJob.ExecutionTime);
            }

            return new JobResult<TTimeJob>(timeJob, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<TTimeJob>(e);
        }
    }

    private async Task<JobResult<TCronJob>> _UpdateCronJobAsync(
        TCronJob? cronJob,
        CancellationToken cancellationToken = default
    )
    {
        if (cronJob is null)
        {
            return new JobResult<TCronJob>(new ArgumentNullException(nameof(cronJob), "Cron job must not be null!"));
        }

        if (
            JobFunctionProvider.JobFunctions.All(x => !string.Equals(x.Key, cronJob.Function, StringComparison.Ordinal))
        )
        {
            return new JobResult<TCronJob>(
                new JobValidatorException($"Cannot find JobFunction with name {cronJob.Function}")
            );
        }

        if (
            _cronScheduleCache.GetNextOccurrenceOrDefault(cronJob.Expression, timeProvider.GetUtcNow().UtcDateTime)
            is not { } nextOccurrence
        )
        {
            return new JobResult<TCronJob>(new JobValidatorException($"Cannot parse expression {cronJob.Expression}"));
        }

        try
        {
            cronJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            var affectedRows = await persistenceProvider
                .UpdateCronJobsAsync([cronJob], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronJob.Id) is { } internalFunction)
            {
                internalFunction.ResetUpdateProps().SetProperty(x => x.ExecutionTime, nextOccurrence);

                await persistenceProvider
                    .UpdateCronJobOccurrenceAsync(internalFunction, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _jobsHostScheduler.Restart();
            }

            _jobsHostScheduler.RestartIfNeeded(nextOccurrence);

            return new JobResult<TCronJob>(cronJob, affectedRows);
        }
        catch (Exception e)
        {
            return new JobResult<TCronJob>(e);
        }
    }

    private async Task<JobResult<TCronJob>> _DeleteCronJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var affectedRows = await persistenceProvider
            .RemoveCronJobsAsync([id], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.ParentId == id))
        {
            _jobsHostScheduler.Restart();
        }

        return new JobResult<TCronJob>(affectedRows);
    }

    private async Task<JobResult<TTimeJob>> _DeleteTimeJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var affectedRows = await persistenceProvider
            .RemoveTimeJobsAsync([id], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.JobId == id))
        {
            _jobsHostScheduler.Restart();
        }

        return new JobResult<TTimeJob>(affectedRows);
    }

    private async Task _RunSchedulePipelineAsync(BaseJobEntity entity, CancellationToken cancellationToken)
    {
        if (!JobFunctionProvider.JobFunctionDescriptors.TryGetValue(entity.Function, out var descriptor))
        {
            throw new JobValidatorException($"Cannot find JobFunction with name {entity.Function}");
        }

        var completed = false;
        JobScheduleNext terminal = _ =>
        {
            completed = true;
            return Task.CompletedTask;
        };

        if (_serviceScopeFactory is null)
        {
            await JobMiddlewareRegistry
                .DispatchScheduleAsync(
                    new(descriptor, entity, EmptyServiceProvider.Instance),
                    terminal,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            await JobMiddlewareRegistry
                .DispatchScheduleAsync(new(descriptor, entity, scope.ServiceProvider), terminal, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!completed)
        {
            throw new JobValidatorException(
                $"Job scheduling middleware did not invoke the terminal delegate for {entity.Function}"
            );
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private DateTime _ConvertToUtcIfNeeded(DateTime dateTime)
    {
        // If DateTime.Kind is Unspecified, assume it's in system timezone
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => TimeZoneInfo.ConvertTimeToUtc(dateTime, _cronScheduleCache.TimeZoneInfo),
            _ => dateTime,
        };
    }

    // Batch operations implementation
    private static void _CacheFunctionReferences(Span<JobExecutionState> functions)
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

    private JobExecutionState[] _BuildImmediateContextsFromNonGeneric(IEnumerable<TimeJobEntity> jobs)
    {
        return [.. jobs.Select(_BuildContextFromNonGeneric)];
    }

    private JobExecutionState _BuildContextFromNonGeneric(TimeJobEntity job)
    {
        var context = new JobExecutionState
        {
            FunctionName = job.Function,
            JobId = job.Id,
            Type = JobType.TimeJob,
            Retries = job.Retries,
            RetryCount = job.RetryCount,
            RetryIntervals = job.RetryIntervals,
            ParentId = job.ParentId,
            ExecutionTime = job.ExecutionTime ?? timeProvider.GetUtcNow().UtcDateTime,
            RunCondition = job.RunCondition ?? RunCondition.OnAnyCompletedStatus,
        };

        context.TimeJobChildren.AddRange(job.Children.Select(_BuildContextFromNonGeneric));

        return context;
    }

    private async Task<List<TTimeJob>> _AddTimeJobsBatchAsync(
        List<TTimeJob>? entities,
        CancellationToken cancellationToken = default
    )
    {
        if (entities == null || entities.Count == 0)
        {
            return entities ?? [];
        }

        var jobFunctionsHashSet = new HashSet<string>(JobFunctionProvider.JobFunctions.Keys, StringComparer.Ordinal);
        var immediateTickers = new List<Guid>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime earliestForNonImmediate = default;
        List<string>? errors = null;
        foreach (var entity in entities)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = guidGenerator.Create();
            }

            if (!jobFunctionsHashSet.Contains(entity.Function))
            {
                // Aggregate every invalid entity and throw once after the loop so the caller sees them all; the batch
                // is all-or-nothing, so a single invalid entity writes nothing.
                (errors ??= []).Add($"Cannot find JobFunction with name {entity.Function}");
                continue;
            }

            await _RunSchedulePipelineAsync(entity, cancellationToken).ConfigureAwait(false);

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

        if (errors is not null)
        {
            throw new JobValidatorException(errors);
        }

        // Synchronous capture before the first await; dead-transaction / mis-wire / write faults propagate (KTD-2).
        var coordinated = _TryCaptureCoordinatedContext();

        if (coordinated is { } context)
        {
            // Route every entity through the seam in insertion order; defer the batch side effects once (KTD-4/R5).
            await context
                .Writer.WriteTimeJobsAsync([.. entities], context.Relational, cancellationToken)
                .ConfigureAwait(false);

            _DeferSideEffects(
                context.Coordinator,
                $"time batch ({entities.Count})",
                ct => _RunTimeJobsBatchSideEffectsAsync(immediateTickers, earliestForNonImmediate, ct)
            );

            return entities;
        }

        await persistenceProvider
            .AddTimeJobsAsync([.. entities], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _RunTimeJobsBatchSideEffectsAsync(immediateTickers, earliestForNonImmediate, cancellationToken)
            .ConfigureAwait(false);

        return entities;
    }

    // Batch time-job side effects: notify-batch first (preserve the existing notify-before-dispatch ordering), then
    // immediate dispatch for due jobs, then scheduler restart for the earliest non-immediate job.
    private async Task _RunTimeJobsBatchSideEffectsAsync(
        List<Guid> immediateTickers,
        DateTime earliestForNonImmediate,
        CancellationToken cancellationToken
    )
    {
        await notificationHubSender.AddTimeJobsBatchNotifyAsync().ConfigureAwait(false);

        // Only try to dispatch immediately if dispatcher is enabled (background services running)
        if (_dispatcher.IsEnabled && immediateTickers.Count > 0)
        {
            var acquired = await persistenceProvider
                .AcquireImmediateTimeJobsAsync([.. immediateTickers], cancellationToken)
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
            _jobsHostScheduler.RestartIfNeeded(earliestForNonImmediate);
        }
    }

    private async Task<List<TCronJob>> _AddCronJobsBatchAsync(
        List<TCronJob> entities,
        CancellationToken cancellationToken = default
    )
    {
        var validEntities = new List<TCronJob>();
        List<string>? errors = null;
        var nextOccurrences = new List<DateTime>();

        foreach (var entity in entities)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = guidGenerator.Create();
            }

            if (
                JobFunctionProvider.JobFunctions.All(x =>
                    !string.Equals(x.Key, entity.Function, StringComparison.Ordinal)
                )
            )
            {
                (errors ??= []).Add($"Cannot find JobFunction with name {entity.Function}");
                continue;
            }

            await _RunSchedulePipelineAsync(entity, cancellationToken).ConfigureAwait(false);

            if (
                _cronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, timeProvider.GetUtcNow().UtcDateTime)
                is not { } nextOccurrence
            )
            {
                (errors ??= []).Add($"Cannot parse expression {entity.Expression}");
                continue;
            }

            entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
            entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            validEntities.Add(entity);
            nextOccurrences.Add(nextOccurrence);
        }

        // Batch is all-or-nothing: any invalid entity aggregates here and throws, writing nothing.
        if (errors is not null)
        {
            throw new JobValidatorException(errors);
        }

        // Synchronous capture before the first await; dead-transaction / mis-wire / write faults propagate (KTD-2).
        var coordinated = _TryCaptureCoordinatedContext();

        if (coordinated is { } context)
        {
            await context
                .Writer.WriteCronJobsAsync([.. validEntities], context.Relational, cancellationToken)
                .ConfigureAwait(false);

            _DeferSideEffects(
                context.Coordinator,
                $"cron batch ({validEntities.Count})",
                ct => _RunCoordinatedCronJobsBatchSideEffectsAsync(context.Writer, validEntities, nextOccurrences, ct)
            );

            return validEntities;
        }

        await persistenceProvider
            .InsertCronJobsAsync([.. validEntities], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (validEntities.Count != 0)
        {
            // Restart scheduler for earliest occurrence
            var earliestOccurrence = nextOccurrences.Min();
            _jobsHostScheduler.RestartIfNeeded(earliestOccurrence);

            // Send notifications for all
            foreach (var entity in validEntities)
            {
                await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);
            }
        }

        return validEntities;
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
            return new JobResult<List<TTimeJob>>(errors[0]);
        }

        try
        {
            var affectedRows = await persistenceProvider
                .UpdateTimeJobsAsync([.. validTickers], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (needsRestart)
            {
                _jobsHostScheduler.Restart();
            }
            else if (validTickers.Count != 0)
            {
                var earliestExecution = validTickers.Min(t => t.ExecutionTime);
                _jobsHostScheduler.RestartIfNeeded(earliestExecution);
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
        var internalFunctionsToUpdate = new List<JobExecutionState>();

        foreach (var cronJob in cronJobs)
        {
            if (cronJob is null)
            {
                errors.Add(new ArgumentNullException(nameof(cronJobs), "Cron job must not be null!"));
                continue;
            }

            if (
                JobFunctionProvider.JobFunctions.All(x =>
                    !string.Equals(x.Key, cronJob.Function, StringComparison.Ordinal)
                )
            )
            {
                errors.Add(new JobValidatorException($"Cannot find JobFunction with name {cronJob.Function}"));
                continue;
            }

            if (
                _cronScheduleCache.GetNextOccurrenceOrDefault(cronJob.Expression, timeProvider.GetUtcNow().UtcDateTime)
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
            return new JobResult<List<TCronJob>>(errors[0]);
        }

        try
        {
            var affectedRows = await persistenceProvider
                .UpdateCronJobsAsync([.. validTickers], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Update internal functions for those that need it
            foreach (var internalFunction in internalFunctionsToUpdate)
            {
                await persistenceProvider
                    .UpdateCronJobOccurrenceAsync(internalFunction, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (needsRestart)
            {
                _jobsHostScheduler.Restart();
            }
            else if (nextOccurrences.Count != 0)
            {
                var earliestOccurrence = nextOccurrences.Min();
                _jobsHostScheduler.RestartIfNeeded(earliestOccurrence);
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
        var affectedRows = await persistenceProvider
            .RemoveTimeJobsAsync([.. ids], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.JobId)))
        {
            _jobsHostScheduler.Restart();
        }

        return new JobResult<TTimeJob>(affectedRows);
    }

    private async Task<JobResult<TCronJob>> _DeleteCronJobsBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await persistenceProvider
            .RemoveCronJobsAsync([.. ids], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.ParentId ?? Guid.Empty)))
        {
            _jobsHostScheduler.Restart();
        }

        return new JobResult<TCronJob>(affectedRows);
    }
}
