using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Headless.Jobs.Managers;

internal class JobsManager<TTimeJob, TCronJob>(
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    IJobsHostScheduler jobsHostScheduler,
    TimeProvider timeProvider,
    IJobsNotificationHubSender notificationHubSender,
    JobsExecutionContext executionContext,
    IJobsDispatcher dispatcher,
    ICurrentCommitCoordinator currentCommitCoordinator
) : ICronJobManager<TCronJob>, ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IJobsHostScheduler _jobsHostScheduler = Argument.IsNotNull(jobsHostScheduler);
    private readonly IJobsDispatcher _dispatcher = Argument.IsNotNull(dispatcher);
    private readonly JobsExecutionContext _executionContext = Argument.IsNotNull(executionContext);
    private readonly ICurrentCommitCoordinator _currentCommitCoordinator = Argument.IsNotNull(
        currentCommitCoordinator
    );

    // Captured ambient coordinator + live relational transaction for one coordinated enqueue. Captured SYNCHRONOUSLY
    // in the caller's frame before the first await — re-reading ICurrentCommitCoordinator.Current after an await could
    // observe a torn-down AsyncLocal scope and silently take the direct path, breaking atomicity.
    private readonly record struct CoordinatedJobContext(
        ICommitCoordinator Coordinator,
        IRelationalCommitContext Relational
    );

    // Routing decision read once, synchronously, before any await (KTD-1):
    //  - null  → no coordinator, or a coordinated scope with no relational capability → today's direct path.
    //  - value → a live relational transaction is present → write rows inside it and defer side effects to commit.
    // Throws when a relational capability is present but its transaction is dead/completed: the caller opened a
    // transaction expecting atomicity, so silent fallback would reintroduce the divergence this feature prevents (KTD-2).
    private CoordinatedJobContext? _TryCaptureCoordinatedContext()
    {
        var coordinator = _currentCommitCoordinator.Current;

        if (coordinator is null)
        {
            return null;
        }

        if (!coordinator.TryGetCapability<IRelationalCommitContext>(out var relational))
        {
            // A coordinated scope without a relational capability (e.g. a messaging-only scope): the coordinator is an
            // ambient scope any subsystem may open, so jobs must not make it infectious — fall back to direct insert.
            return null;
        }

        if (relational.Transaction is null)
        {
            throw new InvalidOperationException(
                "A relational commit coordinator is active but its transaction is no longer live, so the job row "
                    + "cannot be enlisted atomically. Enqueue inside a live coordinated transaction, or call AddAsync "
                    + "outside the coordinated scope."
            );
        }

        return new CoordinatedJobContext(coordinator, relational);
    }

    private ICoordinatedJobWriter<TTimeJob, TCronJob> _RequireCoordinatedWriter()
    {
        if (persistenceProvider is ICoordinatedJobWriter<TTimeJob, TCronJob> writer)
        {
            return writer;
        }

        // Relational coordinator active, but the configured provider cannot write inside the ambient transaction
        // (e.g. the in-memory provider). This is a mis-wire, not a fallback — fail loud rather than insert
        // non-atomically.
        throw new InvalidOperationException(
            "A relational commit coordinator is active but the configured job persistence provider does not support "
                + "coordinated writes. The coordinated-enqueue path requires the EF Core operational store "
                + "(AddOperationalStore)."
        );
    }

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

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.DeleteAsync(
        Guid id,
        CancellationToken cancellationToken
    ) => _DeleteCronJobAsync(id, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.DeleteAsync(
        Guid id,
        CancellationToken cancellationToken
    ) => _DeleteTimeJobAsync(id, cancellationToken);

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

    private async Task<JobResult<TTimeJob>> _AddTimeJobAsync(
        TTimeJob entity,
        CancellationToken cancellationToken
    )
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

        // Capture + writer resolution run BEFORE the try so the fail-loud cases (dead/completed transaction, or a
        // relational coordinator wired to a non-coordinated provider) propagate to the caller (KTD-2) instead of
        // collapsing into a failed JobResult — a swallowed failure here would let the caller's transaction commit
        // without the job row, the exact divergence this feature prevents. Both reads are synchronous (KTD-1).
        var coordinated = _TryCaptureCoordinatedContext();
        var coordinatedWriter = coordinated is null ? null : _RequireCoordinatedWriter();

        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var executionTime = entity.ExecutionTime!.Value;

            if (coordinated is { } context)
            {
                // Write the row inside the caller's transaction; defer dispatch/scheduler/notify to commit (KTD-4).
                // JobResult.IsSucceeded here means the row was buffered into the transaction — it guarantees the row
                // committed, not that the deferred dispatch ran. A post-commit dispatch failure is recovered by the
                // scheduler's polling sweep.
                await coordinatedWriter!
                    .WriteTimeJobsAsync([entity], context.Relational, cancellationToken)
                    .ConfigureAwait(false);

                context.Coordinator.OnCommit(
                    (_, ct) =>
                        new ValueTask(_RunTimeJobSideEffectsAsync(entity, now, executionTime, ct))
                );

                return new JobResult<TTimeJob>(entity);
            }

            // Direct path (no coordinator / non-relational scope): persist then run side effects in-band.
            await persistenceProvider.AddTimeJobs([entity], cancellationToken: cancellationToken);
            await _RunTimeJobSideEffectsAsync(entity, now, executionTime, cancellationToken)
                .ConfigureAwait(false);

            return new JobResult<TTimeJob>(entity);
        }
        catch (Exception e)
        {
            return new JobResult<TTimeJob>(e);
        }
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

    private async Task<JobResult<TCronJob>> _AddCronJobAsync(
        TCronJob entity,
        CancellationToken cancellationToken
    )
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
            CronScheduleCache.GetNextOccurrenceOrDefault(
                entity.Expression,
                timeProvider.GetUtcNow().UtcDateTime
            )
            is not { } nextOccurrence
        )
        {
            return new JobResult<TCronJob>(
                new JobValidatorException($"Cannot parse expression {entity.Expression}")
            );
        }

        entity.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Fail loud before the try (see _AddTimeJobAsync) so dead-transaction / mis-wire propagate (KTD-2).
        var coordinated = _TryCaptureCoordinatedContext();
        var coordinatedWriter = coordinated is null ? null : _RequireCoordinatedWriter();

        try
        {
            if (coordinated is { } context)
            {
                await coordinatedWriter!
                    .WriteCronJobsAsync([entity], context.Relational, cancellationToken)
                    .ConfigureAwait(false);

                // Cron has no immediate-dispatch branch; defer cache-invalidation + scheduler-restart + notify (KTD-4).
                context.Coordinator.OnCommit(
                    (_, ct) =>
                        new ValueTask(
                            _RunCoordinatedCronJobSideEffectsAsync(
                                coordinatedWriter,
                                entity,
                                nextOccurrence,
                                ct
                            )
                        )
                );

                return new JobResult<TCronJob>(entity);
            }

            await persistenceProvider.InsertCronJobs(
                [entity],
                cancellationToken: cancellationToken
            );

            _jobsHostScheduler.RestartIfNeeded(nextOccurrence);

            await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);

            return new JobResult<TCronJob>(entity);
        }
        catch (Exception e)
        {
            return new JobResult<TCronJob>(e);
        }
    }

    // Coordinated single-cron side effects, deferred to commit. The coordinated write is a pure row write, so the
    // cron-expressions cache invalidation (which the direct path's InsertCronJobs runs after SaveChanges) must fire
    // here — post-commit — never on a pre-commit snapshot (KTD-4).
    private async Task _RunCoordinatedCronJobSideEffectsAsync(
        ICoordinatedJobWriter<TTimeJob, TCronJob> writer,
        TCronJob entity,
        DateTime nextOccurrence,
        CancellationToken cancellationToken
    )
    {
        await writer.InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);
        _jobsHostScheduler.RestartIfNeeded(nextOccurrence);
        await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);
    }

    private async Task<JobResult<TTimeJob>> _UpdateTimeJobAsync(
        TTimeJob timeJob,
        CancellationToken cancellationToken
    )
    {
        if (timeJob is null)
        {
            return new JobResult<TTimeJob>(new JobValidatorException($"Job must not be null!"));
        }

        if (timeJob.ExecutionTime == null)
        {
            return new JobResult<TTimeJob>(
                new JobValidatorException($"Job ExecutionTime must not be null!")
            );
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
        TCronJob cronJob,
        CancellationToken cancellationToken = default
    )
    {
        if (cronJob is null)
        {
            return new JobResult<TCronJob>(
                new ArgumentNullException(nameof(cronJob), @"Cron job must not be null!")
            );
        }

        if (JobFunctionProvider.JobFunctions.All(x => x.Key != cronJob.Function))
        {
            return new JobResult<TCronJob>(
                new JobValidatorException($"Cannot find JobFunction with name {cronJob.Function}")
            );
        }

        if (
            CronScheduleCache.GetNextOccurrenceOrDefault(
                cronJob.Expression,
                timeProvider.GetUtcNow().UtcDateTime
            )
            is not { } nextOccurrence
        )
        {
            return new JobResult<TCronJob>(
                new JobValidatorException($"Cannot parse expression {cronJob.Expression}")
            );
        }

        try
        {
            cronJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            var affectedRows = await persistenceProvider.UpdateCronJobs(
                [cronJob],
                cancellationToken: cancellationToken
            );

            if (
                _executionContext.Functions.FirstOrDefault(x => x.ParentId == cronJob.Id) is
                { } internalFunction
            )
            {
                internalFunction
                    .ResetUpdateProps()
                    .SetProperty(x => x.ExecutionTime, nextOccurrence);

                await persistenceProvider
                    .UpdateCronJobOccurrence(internalFunction, cancellationToken: cancellationToken)
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

    private async Task<JobResult<TCronJob>> _DeleteCronJobAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await persistenceProvider.RemoveCronJobs(
            [id],
            cancellationToken: cancellationToken
        );

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.ParentId == id))
        {
            _jobsHostScheduler.Restart();
        }

        return new JobResult<TCronJob>(affectedRows);
    }

    private async Task<JobResult<TTimeJob>> _DeleteTimeJobAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var affectedRows = await persistenceProvider.RemoveTimeJobs(
            [id],
            cancellationToken: cancellationToken
        );

        if (affectedRows > 0 && _executionContext.Functions.Any(x => x.JobId == id))
        {
            _jobsHostScheduler.Restart();
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
            DateTimeKind.Unspecified => TimeZoneInfo.ConvertTimeToUtc(
                dateTime,
                CronScheduleCache.TimeZoneInfo
            ),
            _ => dateTime,
        };
    }

    // Batch operations implementation
    private static void _CacheFunctionReferences(Span<InternalFunctionContext> functions)
    {
        for (var i = 0; i < functions.Length; i++)
        {
            ref var context = ref functions[i];
            if (
                JobFunctionProvider.JobFunctions.TryGetValue(
                    context.FunctionName,
                    out var tickerItem
                )
            )
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

    private InternalFunctionContext[] _BuildImmediateContextsFromNonGeneric(
        IEnumerable<TimeJobEntity> jobs
    )
    {
        return jobs.Select(_BuildContextFromNonGeneric).ToArray();
    }

    private InternalFunctionContext _BuildContextFromNonGeneric(TimeJobEntity job)
    {
        return new InternalFunctionContext
        {
            FunctionName = job.Function,
            JobId = job.Id,
            Type = JobType.TimeJob,
            Retries = job.Retries,
            RetryIntervals = job.RetryIntervals,
            ParentId = job.ParentId,
            ExecutionTime = job.ExecutionTime ?? timeProvider.GetUtcNow().UtcDateTime,
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
            return new JobResult<List<TTimeJob>>(entities ?? []);
        }

        var jobFunctionsHashSet = new HashSet<string>(
            JobFunctionProvider.JobFunctions.Keys,
            StringComparer.Ordinal
        );
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
                    new JobValidatorException(
                        $"Cannot find JobFunction with name {entity.Function}"
                    )
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
            else if (
                earliestForNonImmediate == default
                || entity.ExecutionTime <= earliestForNonImmediate
            )
            {
                earliestForNonImmediate = entity.ExecutionTime.Value;
            }
        }

        // Fail loud before the try (see _AddTimeJobAsync) so dead-transaction / mis-wire propagate (KTD-2).
        var coordinated = _TryCaptureCoordinatedContext();
        var coordinatedWriter = coordinated is null ? null : _RequireCoordinatedWriter();

        try
        {
            if (coordinated is { } context)
            {
                // Route every entity through the seam in insertion order; defer the batch side effects once (KTD-4/R5).
                await coordinatedWriter!
                    .WriteTimeJobsAsync(entities.ToArray(), context.Relational, cancellationToken)
                    .ConfigureAwait(false);

                context.Coordinator.OnCommit(
                    (_, ct) =>
                        new ValueTask(
                            _RunTimeJobsBatchSideEffectsAsync(
                                immediateTickers,
                                earliestForNonImmediate,
                                ct
                            )
                        )
                );

                return new JobResult<List<TTimeJob>>(entities);
            }

            await persistenceProvider.AddTimeJobs(
                entities.ToArray(),
                cancellationToken: cancellationToken
            );
            await _RunTimeJobsBatchSideEffectsAsync(
                    immediateTickers,
                    earliestForNonImmediate,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return new JobResult<List<TTimeJob>>(entities);
        }
        catch (Exception e)
        {
            return new JobResult<List<TTimeJob>>(e);
        }
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
            _jobsHostScheduler.RestartIfNeeded(earliestForNonImmediate);
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
                errors.Add(
                    new JobValidatorException(
                        $"Cannot find JobFunction with name {entity.Function}"
                    )
                );
                continue;
            }

            if (
                CronScheduleCache.GetNextOccurrenceOrDefault(
                    entity.Expression,
                    timeProvider.GetUtcNow().UtcDateTime
                )
                is not { } nextOccurrence
            )
            {
                errors.Add(
                    new JobValidatorException($"Cannot parse expression {entity.Expression}")
                );
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

        // Fail loud before the try (see _AddTimeJobAsync) so dead-transaction / mis-wire propagate (KTD-2).
        var coordinated = _TryCaptureCoordinatedContext();
        var coordinatedWriter = coordinated is null ? null : _RequireCoordinatedWriter();

        try
        {
            if (coordinated is { } context)
            {
                await coordinatedWriter!
                    .WriteCronJobsAsync(
                        validEntities.ToArray(),
                        context.Relational,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                context.Coordinator.OnCommit(
                    (_, ct) =>
                        new ValueTask(
                            _RunCoordinatedCronJobsBatchSideEffectsAsync(
                                coordinatedWriter,
                                validEntities,
                                nextOccurrences,
                                ct
                            )
                        )
                );

                return new JobResult<List<TCronJob>>(validEntities);
            }

            await persistenceProvider.InsertCronJobs(
                validEntities.ToArray(),
                cancellationToken: cancellationToken
            );

            if (validEntities.Count != 0)
            {
                // Restart scheduler for earliest occurrence
                var earliestOccurrence = nextOccurrences.Min();
                _jobsHostScheduler.RestartIfNeeded(earliestOccurrence);

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

    // Coordinated batch-cron side effects, deferred to commit (cache invalidation post-commit per KTD-4).
    private async Task _RunCoordinatedCronJobsBatchSideEffectsAsync(
        ICoordinatedJobWriter<TTimeJob, TCronJob> writer,
        List<TCronJob> validEntities,
        List<DateTime> nextOccurrences,
        CancellationToken cancellationToken
    )
    {
        await writer.InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        if (validEntities.Count != 0)
        {
            var earliestOccurrence = nextOccurrences.Min();
            _jobsHostScheduler.RestartIfNeeded(earliestOccurrence);

            foreach (var entity in validEntities)
            {
                await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);
            }
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
        var internalFunctionsToUpdate = new List<InternalFunctionContext>();

        foreach (var cronJob in cronJobs)
        {
            if (cronJob is null)
            {
                errors.Add(
                    new ArgumentNullException(nameof(cronJobs), @"Cron job must not be null!")
                );
                continue;
            }

            if (JobFunctionProvider.JobFunctions.All(x => x.Key != cronJob.Function))
            {
                errors.Add(
                    new JobValidatorException(
                        $"Cannot find JobFunction with name {cronJob.Function}"
                    )
                );
                continue;
            }

            if (
                CronScheduleCache.GetNextOccurrenceOrDefault(
                    cronJob.Expression,
                    timeProvider.GetUtcNow().UtcDateTime
                )
                is not { } nextOccurrence
            )
            {
                errors.Add(
                    new JobValidatorException($"Cannot parse expression {cronJob.Expression}")
                );
                continue;
            }

            cronJob.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            if (
                _executionContext.Functions.FirstOrDefault(x => x.ParentId == cronJob.Id) is
                { } internalFunction
            )
            {
                internalFunction
                    .ResetUpdateProps()
                    .SetProperty(x => x.ExecutionTime, nextOccurrence);
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
        var affectedRows = await persistenceProvider.RemoveTimeJobs(
            ids.ToArray(),
            cancellationToken: cancellationToken
        );

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
        var affectedRows = await persistenceProvider.RemoveCronJobs(
            ids.ToArray(),
            cancellationToken: cancellationToken
        );

        if (
            affectedRows > 0
            && _executionContext.Functions.Any(x => ids.Contains(x.ParentId ?? Guid.Empty))
        )
        {
            _jobsHostScheduler.Restart();
        }

        return new JobResult<TCronJob>(affectedRows);
    }
}
