using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Provider;

internal sealed class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob> : IJobPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly ConcurrentDictionary<Guid, TTimeJob> _TimeJobs = new();

    // Index of parent -> child ids for fast hierarchy lookup in memory
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _ChildrenIndex = new();

    private readonly ConcurrentDictionary<Guid, TCronJob> _CronJobs = new();

    private readonly ConcurrentDictionary<Guid, CronJobOccurrenceEntity<TCronJob>> _CronOccurrences = new();

    private readonly TimeProvider _timeProvider;
    private readonly string _lockHolder;

    public JobsInMemoryPersistenceProvider(IServiceProvider serviceProvider)
    {
        _timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
        _lockHolder = optionsBuilder?.NodeIdentifier ?? Environment.MachineName;
    }

    #region Time Job Methods

    public async IAsyncEnumerable<TimeJobEntity> QueueTimeJobs(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var timeJob in timeJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_TimeJobs.TryGetValue(timeJob.Id, out var existingTicker))
            {
                // Check if we can update (similar to optimistic concurrency)
                if (existingTicker.UpdatedAt == timeJob.UpdatedAt)
                {
                    // Update the job
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.LockHolder = _lockHolder;
                    updatedTicker.LockedAt = now;
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = JobStatus.Queued;

                    if (_TimeJobs.TryUpdate(timeJob.Id, updatedTicker, existingTicker))
                    {
                        timeJob.UpdatedAt = now;
                        timeJob.LockHolder = _lockHolder;
                        timeJob.LockedAt = now;
                        timeJob.Status = JobStatus.Queued;

                        yield return timeJob;
                    }
                }
            }
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobs(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        // First, get the time jobs that need to be updated (matching EF query)
        // NOTE: we project to the raw job here and only build the full
        //       TimeJobEntity graph after we successfully acquire the lock.
        var timeJobsToUpdate = _TimeJobs
            .Values.Where(x => x.ExecutionTime != null)
            .Where(x => x.Status is JobStatus.Idle or JobStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .ToArray();

        foreach (var job in timeJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Now update the actual job in storage
            if (_TimeJobs.TryGetValue(job.Id, out var existingTicker))
            {
                // Check if we can update (matching EF's Where condition)
                if (existingTicker.UpdatedAt <= job.UpdatedAt)
                {
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.LockHolder = _lockHolder;
                    updatedTicker.LockedAt = now;
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = JobStatus.InProgress;

                    if (_TimeJobs.TryUpdate(job.Id, updatedTicker, existingTicker))
                    {
                        // Only build the full hierarchy for successfully acquired jobs
                        yield return _ForQueueTimeJobs(job);
                    }
                }
            }
        }
    }

    public Task ReleaseAcquiredTimeJobs(Guid[] timeJobIds, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var idsToRelease = timeJobIds.Length == 0 ? _TimeJobs.Keys.ToArray() : timeJobIds;

        foreach (var id in idsToRelease)
        {
            if (_TimeJobs.TryGetValue(id, out var job))
            {
                // Check if we can release (similar to WhereCanAcquire)
                if (_CanAcquire(job))
                {
                    var updatedTicker = _CloneTicker(job);
                    updatedTicker.LockHolder = null;
                    updatedTicker.LockedAt = null;
                    updatedTicker.Status = JobStatus.Idle;
                    updatedTicker.UpdatedAt = now;

                    _TimeJobs.TryUpdate(id, updatedTicker, job);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<TimeJobEntity[]> GetEarliestTimeJobs(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var oneSecondAgo = now.AddSeconds(-1);

        // Base query: same filter as EF provider, but over the snapshot
        var baseQuery = _TimeJobs
            .Values.Where(x => x.ExecutionTime != null)
            .Where(_CanAcquire)
            .Where(x => x.ExecutionTime >= oneSecondAgo)
            .ToArray();

        // Get minimum execution time
        var minExecutionTime = baseQuery.OrderBy(x => x.ExecutionTime).Select(x => x.ExecutionTime).FirstOrDefault();

        if (minExecutionTime == null)
        {
            return Task.FromResult(Array.Empty<TimeJobEntity>());
        }

        // Round the minimum execution time down to its second
        var minSecond = new DateTime(
            minExecutionTime.Value.Year,
            minExecutionTime.Value.Month,
            minExecutionTime.Value.Day,
            minExecutionTime.Value.Hour,
            minExecutionTime.Value.Minute,
            minExecutionTime.Value.Second,
            DateTimeKind.Utc
        );

        var maxExecutionTime = minSecond.AddSeconds(1);

        // Fetch all jobs within that complete second and map using the children lookup
        var result = baseQuery
            .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
            .OrderBy(x => x.ExecutionTime)
            .Select(_ForQueueTimeJobs)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<int> UpdateTimeJob(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        if (_TimeJobs.TryGetValue(functionContext.JobId, out var job))
        {
            var updatedTicker = _CloneTicker(job);
            _ApplyFunctionContextToTicker(updatedTicker, functionContext);

            if (_TimeJobs.TryUpdate(functionContext.JobId, updatedTicker, job))
            {
                return Task.FromResult(1);
            }
        }

        return Task.FromResult(0);
    }

    public Task<byte[]> GetTimeJobRequest(Guid id, CancellationToken cancellationToken)
    {
        if (_TimeJobs.TryGetValue(id, out var job))
        {
            return Task.FromResult(job.Request ?? []);
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    public Task UpdateTimeJobsWithUnifiedContext(
        Guid[] timeJobIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var id in timeJobIds)
        {
            if (_TimeJobs.TryGetValue(id, out var job))
            {
                var updatedTicker = _CloneTicker(job);
                _ApplyFunctionContextToTicker(updatedTicker, functionContext);
                _TimeJobs.TryUpdate(id, updatedTicker, job);
            }
        }

        return Task.CompletedTask;
    }

    public Task<TimeJobEntity[]> AcquireImmediateTimeJobsAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids == null || ids.Length == 0)
        {
            return Task.FromResult(Array.Empty<TimeJobEntity>());
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var acquired = new List<TimeJobEntity>();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_TimeJobs.TryGetValue(id, out var job))
            {
                continue;
            }

            if (!_CanAcquire(job))
            {
                continue;
            }

            var updatedTicker = _CloneTicker(job);
            updatedTicker.LockHolder = _lockHolder;
            updatedTicker.LockedAt = now;
            updatedTicker.Status = JobStatus.InProgress;
            updatedTicker.UpdatedAt = now;

            if (_TimeJobs.TryUpdate(id, updatedTicker, job))
            {
                acquired.Add(_ForQueueTimeJobs(updatedTicker));
            }
        }

        return Task.FromResult(acquired.ToArray());
    }

    public Task<TTimeJob?> GetTimeJobById(Guid id, CancellationToken cancellationToken = default)
    {
        if (_TimeJobs.TryGetValue(id, out var job))
        {
            var result = _BuildTickerHierarchy(job);
            return Task.FromResult<TTimeJob?>(result);
        }

        return Task.FromResult<TTimeJob?>(null);
    }

    public Task<TTimeJob[]> GetTimeJobs(
        Expression<Func<TTimeJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _TimeJobs.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        // Match EF Core - only return root items (ParentId == null) with nested children
        var results = query
            .Where(x => x.ParentId == null) // Only root items, matching EF Core
            .OrderByDescending(x => x.ExecutionTime) // Match EF Core's OrderByDescending(x => x.ExecutionTime)
            .Select(_BuildTickerHierarchy)
            .ToArray();

        return Task.FromResult(results);
    }

    public Task<PaginationResult<TTimeJob>> GetTimeJobsPaginated(
        Expression<Func<TTimeJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _TimeJobs.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        // Match EF Core - only count and paginate root items
        query = query.Where(x => x.ParentId == null);

        var totalCount = query.Count();

        var items = query
            .OrderByDescending(x => x.ExecutionTime) // Match EF Core's OrderByDescending(x => x.ExecutionTime)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(_BuildTickerHierarchy)
            .ToArray();

        return Task.FromResult(
            new PaginationResult<TTimeJob>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
            }
        );
    }

    public Task<int> AddTimeJobs(TTimeJob[] jobs, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var job in jobs)
        {
            count += _AddTickerWithChildren(job);
        }

        return Task.FromResult(count);
    }

    private int _AddTickerWithChildren(TTimeJob job, Guid? parentId = null)
    {
        var count = 0;

        // Set the parent ID if this is a child
        if (parentId.HasValue)
        {
            job.ParentId = parentId.Value;
        }

        // Add the job itself
        if (_TimeJobs.TryAdd(job.Id, job))
        {
            // Maintain children index
            if (job.ParentId.HasValue)
            {
                _AddChildIndex(job.ParentId.Value, job.Id);
            }

            count++;

            // Recursively add all children
            if (job.Children is { Count: > 0 })
            {
                foreach (var child in job.Children)
                {
                    // Cast to TTimeJob since Children is ICollection<TTimeJob>
                    if (child is { } childTicker)
                    {
                        count += _AddTickerWithChildren(childTicker, job.Id);
                    }
                }
            }
        }

        return count;
    }

    public Task<int> UpdateTimeJobs(TTimeJob[] jobs, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var job in jobs)
        {
            count += _UpdateTickerWithChildren(job);
        }

        return Task.FromResult(count);
    }

    private int _UpdateTickerWithChildren(TTimeJob job, Guid? parentId = null)
    {
        var count = 0;

        // Set the parent ID if this is a child
        if (parentId.HasValue)
        {
            job.ParentId = parentId.Value;
        }

        // Update the job itself
        if (_TimeJobs.TryGetValue(job.Id, out var existing))
        {
            if (_TimeJobs.TryUpdate(job.Id, job, existing))
            {
                // Maintain children index for parent changes
                if (existing.ParentId != job.ParentId)
                {
                    if (existing.ParentId.HasValue)
                    {
                        _RemoveChildIndex(existing.ParentId.Value, job.Id);
                    }

                    if (job.ParentId.HasValue)
                    {
                        _AddChildIndex(job.ParentId.Value, job.Id);
                    }
                }

                count++;

                // Recursively update all children
                if (job.Children is { Count: > 0 })
                {
                    foreach (var child in job.Children)
                    {
                        // Cast to TTimeJob since Children is ICollection<TTimeJob>
                        if (child is { } childTicker)
                        {
                            count += _UpdateTickerWithChildren(childTicker, job.Id);
                        }
                    }
                }
            }
        }
        else
        {
            // If it doesn't exist, add it (this can happen for new children)
            count += _AddTickerWithChildren(job, parentId);
        }

        return count;
    }

    public Task<int> RemoveTimeJobs(Guid[] jobIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in jobIds)
        {
            // Remove job and all its children (cascade delete)
            if (_TimeJobs.TryRemove(id, out var removed))
            {
                count++;

                // Clean children index
                if (removed.ParentId.HasValue)
                {
                    _RemoveChildIndex(removed.ParentId.Value, removed.Id);
                }

                // Remove children
                var childrenIds = _GetChildrenIds(id);

                foreach (var childId in childrenIds)
                {
                    if (_TimeJobs.TryRemove(childId, out var child))
                    {
                        count++;
                        if (child.ParentId.HasValue)
                        {
                            _RemoveChildIndex(child.ParentId.Value, child.Id);
                        }
                    }
                }
            }
        }

        return Task.FromResult(count);
    }

    public Task ReleaseDeadNodeTimeJobResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Phase 1: release acquirable jobs for the dead node (match EF WhereCanAcquire(instanceIdentifier))
        var releasable = _TimeJobs
            .Values.Where(x =>
                (x.Status == JobStatus.Idle || x.Status == JobStatus.Queued)
                && (x.LockHolder == instanceIdentifier || x.LockedAt == null)
            )
            .ToArray();

        foreach (var job in releasable)
        {
            if (!_TimeJobs.TryGetValue(job.Id, out var currentTicker))
            {
                continue;
            }

            var updatedTicker = _CloneTicker(currentTicker);
            updatedTicker.LockHolder = null;
            updatedTicker.LockedAt = null;
            updatedTicker.Status = JobStatus.Idle;
            updatedTicker.UpdatedAt = now;

            _TimeJobs.TryUpdate(job.Id, updatedTicker, currentTicker);
        }

        // Phase 2: mark in-progress jobs for that node as skipped
        var inProgress = _TimeJobs
            .Values.Where(x => x.LockHolder == instanceIdentifier && x.Status == JobStatus.InProgress)
            .ToArray();

        foreach (var job in inProgress)
        {
            if (!_TimeJobs.TryGetValue(job.Id, out var currentTicker))
            {
                continue;
            }

            var updatedTicker = _CloneTicker(currentTicker);
            updatedTicker.Status = JobStatus.Skipped;
            updatedTicker.SkippedReason = "Node is not alive!";
            updatedTicker.ExecutedAt = now;
            updatedTicker.UpdatedAt = now;

            _TimeJobs.TryUpdate(job.Id, updatedTicker, currentTicker);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Cron Job Methods

    public Task MigrateDefinedCronJobs(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var (function, expression) in cronJobs)
        {
            // Check if already exists (take snapshot for thread safety)
            var exists = _CronJobs.Values.ToArray().Any(x => x.Function == function && x.Expression == expression);
            if (!exists)
            {
                var id = Guid.NewGuid();
                var cronJob = new TCronJob
                {
                    Id = id,
                    Function = function,
                    Expression = expression,
                    InitIdentifier = $"MemoryTicker_Seeded_{id}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = [],
                };

                _CronJobs.TryAdd(id, cronJob);
            }
        }

        return Task.CompletedTask;
    }

    public Task<CronJobEntity[]> GetAllCronJobExpressions(CancellationToken cancellationToken)
    {
        var result = _CronJobs.Values.Cast<CronJobEntity>().ToArray();

        return Task.FromResult(result);
    }

    public Task<TCronJob?> GetCronJobById(Guid id, CancellationToken cancellationToken)
    {
        _CronJobs.TryGetValue(id, out var job);

        return Task.FromResult(job);
    }

    public Task<TCronJob[]> GetCronJobs(
        Expression<Func<TCronJob, bool>>? predicate,
        CancellationToken cancellationToken
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronJobs.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var results = query.OrderByDescending(x => x.CreatedAt).ToArray();

        return Task.FromResult(results);
    }

    public Task<PaginationResult<TCronJob>> GetCronJobsPaginated(
        Expression<Func<TCronJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronJobs.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var totalCount = query.Count();

        var items = query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(
            new PaginationResult<TCronJob>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
            }
        );
    }

    public Task<int> InsertCronJobs(TCronJob[] jobs, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var job in jobs)
        {
            if (_CronJobs.TryAdd(job.Id, job))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> UpdateCronJobs(TCronJob[] cronJob, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var job in cronJob)
        {
            if (_CronJobs.TryGetValue(job.Id, out var existing))
            {
                if (_CronJobs.TryUpdate(job.Id, job, existing))
                {
                    count++;
                }
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> RemoveCronJobs(Guid[] cronJobIds, CancellationToken cancellationToken)
    {
        var count = cronJobIds.Count(id => _CronJobs.TryRemove(id, out _));

        return Task.FromResult(count);
    }

    #endregion

    #region Cron Occurrence Methods

    public Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrence(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var mainSchedulerThreshold = now.AddSeconds(-1); // Main scheduler handles items within the 1-second window

        var query = _CronOccurrences.Values.AsEnumerable();

        if (ids is { Length: > 0 })
        {
            query = query.Where(x => ids.Contains(x.CronJobId));
        }

        var occurrence = query
            .Where(_CanAcquireCronOccurrence)
            .Where(x => x.ExecutionTime >= mainSchedulerThreshold) // Only recent/upcoming tasks (not heavily overdue)
            .OrderBy(x => x.ExecutionTime)
            .FirstOrDefault();

        return Task.FromResult(occurrence!);
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueCronJobOccurrences(
        (DateTime Key, InternalManagerContext[] Items) cronJobOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var context in cronJobOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each cron occurrence should have a unique ID
            var occurrenceId = context.NextCronOccurrence?.Id ?? Guid.NewGuid();

            // Check if this specific occurrence already exists
            if (_CronOccurrences.TryGetValue(occurrenceId, out var existingOccurrence))
            {
                // Update existing occurrence (should be rare - only if re-queuing)
                var updatedOccurrence = _CloneCronOccurrence(existingOccurrence);
                updatedOccurrence.LockHolder = _lockHolder;
                updatedOccurrence.LockedAt = now;
                updatedOccurrence.UpdatedAt = now;
                updatedOccurrence.Status = JobStatus.Queued;

                if (_CronOccurrences.TryUpdate(occurrenceId, updatedOccurrence, existingOccurrence))
                {
                    yield return updatedOccurrence;
                }
            }
            else
            {
                // Create new occurrence (normal case - each execution time gets its own occurrence)
                var newOccurrence = new CronJobOccurrenceEntity<TCronJob>
                {
                    Id = occurrenceId,
                    CronJobId = context.Id,
                    ExecutionTime = cronJobOccurrences.Key,
                    Status = JobStatus.Queued,
                    LockHolder = _lockHolder,
                    LockedAt = now,
                    CreatedAt = context.NextCronOccurrence?.CreatedAt ?? now,
                    UpdatedAt = now,
                    RetryCount = 0,
                };

                // Try to get the cron job
                if (_CronJobs.TryGetValue(context.Id, out var cronJob))
                {
                    newOccurrence.CronJob = cronJob;
                }

                if (_CronOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                {
                    yield return newOccurrence;
                }
            }
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueTimedOutCronJobOccurrences(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        var occurrencesToUpdate = _CronOccurrences
            .Values.Where(x => x.Status is JobStatus.Idle or JobStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .ToArray();

        foreach (var occurrence in occurrencesToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_CronOccurrences.TryGetValue(occurrence.Id, out var existingOccurrence))
            {
                if (existingOccurrence.UpdatedAt <= occurrence.UpdatedAt)
                {
                    var updatedOccurrence = _CloneCronOccurrence(existingOccurrence);
                    updatedOccurrence.LockHolder = _lockHolder;
                    updatedOccurrence.LockedAt = now;
                    updatedOccurrence.UpdatedAt = now;
                    updatedOccurrence.Status = JobStatus.InProgress;

                    if (_CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, existingOccurrence))
                    {
                        yield return updatedOccurrence;
                    }
                }
            }
        }
    }

    public Task UpdateCronJobOccurrence(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        if (_CronOccurrences.TryGetValue(functionContext.JobId, out var occurrence))
        {
            var updatedOccurrence = _CloneCronOccurrence(occurrence);
            _ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);

            _CronOccurrences.TryUpdate(functionContext.JobId, updatedOccurrence, occurrence);
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAcquiredCronJobOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var idsToRelease = occurrenceIds.Length == 0 ? _CronOccurrences.Keys.ToArray() : occurrenceIds;

        foreach (var id in idsToRelease)
        {
            if (_CronOccurrences.TryGetValue(id, out var occurrence))
            {
                if (_CanAcquireCronOccurrence(occurrence))
                {
                    var updatedOccurrence = _CloneCronOccurrence(occurrence);
                    updatedOccurrence.LockHolder = null;
                    updatedOccurrence.LockedAt = null;
                    updatedOccurrence.Status = JobStatus.Idle;
                    updatedOccurrence.UpdatedAt = now;

                    _CronOccurrences.TryUpdate(id, updatedOccurrence, occurrence);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<byte[]> GetCronJobOccurrenceRequest(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Cron job occurrences don't have their own request, get it from the cron job
        if (_CronOccurrences.TryGetValue(jobId, out var occurrence))
        {
            if (occurrence.CronJob != null)
            {
                return Task.FromResult(occurrence.CronJob.Request ?? []);
            }

            if (_CronJobs.TryGetValue(occurrence.CronJobId, out var cronJob))
            {
                return Task.FromResult(cronJob.Request ?? []);
            }
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    public Task UpdateCronJobOccurrencesWithUnifiedContext(
        Guid[] timeJobIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var id in timeJobIds)
        {
            if (_CronOccurrences.TryGetValue(id, out var occurrence))
            {
                var updatedOccurrence = _CloneCronOccurrence(occurrence);
                _ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);
                _CronOccurrences.TryUpdate(id, updatedOccurrence, occurrence);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReleaseDeadNodeOccurrenceResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Phase 1: release acquirable occurrences for the dead node (match EF WhereCanAcquire(instanceIdentifier))
        var releasable = _CronOccurrences
            .Values.Where(x =>
                (x.Status == JobStatus.Idle || x.Status == JobStatus.Queued)
                && (x.LockHolder == instanceIdentifier || x.LockedAt == null)
            )
            .ToArray();

        foreach (var occurrence in releasable)
        {
            if (!_CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
            {
                continue;
            }

            var updatedOccurrence = _CloneCronOccurrence(currentOccurrence);
            updatedOccurrence.LockHolder = null;
            updatedOccurrence.LockedAt = null;
            updatedOccurrence.Status = JobStatus.Idle;
            updatedOccurrence.UpdatedAt = now;

            _CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, currentOccurrence);
        }

        // Phase 2: mark in-progress occurrences for that node as skipped
        var inProgress = _CronOccurrences
            .Values.Where(x => x.LockHolder == instanceIdentifier && x.Status == JobStatus.InProgress)
            .ToArray();

        foreach (var occurrence in inProgress)
        {
            if (!_CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
            {
                continue;
            }

            var updatedOccurrence = _CloneCronOccurrence(currentOccurrence);
            updatedOccurrence.Status = JobStatus.Skipped;
            updatedOccurrence.SkippedReason = "Node is not alive!";
            updatedOccurrence.ExecutedAt = now;
            updatedOccurrence.UpdatedAt = now;

            _CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, currentOccurrence);
        }

        return Task.CompletedTask;
    }

    public Task<CronJobOccurrenceEntity<TCronJob>[]> GetAllCronJobOccurrences(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronOccurrences.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var results = query.OrderByDescending(x => x.CreatedAt).ToArray();

        return Task.FromResult(results);
    }

    public Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetAllCronJobOccurrencesPaginated(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>> predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronOccurrences.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var totalCount = query.Count();

        var items = query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(
            new PaginationResult<CronJobOccurrenceEntity<TCronJob>>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
            }
        );
    }

    public Task<int> InsertCronJobOccurrences(
        CronJobOccurrenceEntity<TCronJob>[] cronJobOccurrences,
        CancellationToken cancellationToken
    )
    {
        var count = 0;
        foreach (var occurrence in cronJobOccurrences)
        {
            // Ensure navigation is populated for in-memory usage
            if (occurrence.CronJob == null && _CronJobs.TryGetValue(occurrence.CronJobId, out var cronJob))
            {
                occurrence.CronJob = cronJob;
            }

            if (_CronOccurrences.TryAdd(occurrence.Id, occurrence))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> RemoveCronJobOccurrences(Guid[] cronJobOccurrences, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var id in cronJobOccurrences)
        {
            if (_CronOccurrences.TryRemove(id, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<CronJobOccurrenceEntity<TCronJob>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        if (occurrenceIds == null || occurrenceIds.Length == 0)
        {
            return Task.FromResult(Array.Empty<CronJobOccurrenceEntity<TCronJob>>());
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var acquired = new List<CronJobOccurrenceEntity<TCronJob>>();

        foreach (var id in occurrenceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_CronOccurrences.TryGetValue(id, out var occurrence))
            {
                continue;
            }

            if (!_CanAcquireCronOccurrence(occurrence))
            {
                continue;
            }

            var updated = _CloneCronOccurrence(occurrence);
            updated.LockHolder = _lockHolder;
            updated.LockedAt = now;
            updated.Status = JobStatus.InProgress;
            updated.UpdatedAt = now;

            if (_CronOccurrences.TryUpdate(id, updated, occurrence))
            {
                acquired.Add(updated);
            }
        }

        return Task.FromResult(acquired.ToArray());
    }

    #endregion

    #region Helper Methods

    private TTimeJob _BuildTickerHierarchy(TTimeJob job)
    {
        var root = _CloneTicker(job);
        root.Children = _BuildChildrenHierarchy(job.Id);
        return root;
    }

    private List<TTimeJob> _BuildChildrenHierarchy(Guid parentId)
    {
        if (!_ChildrenIndex.TryGetValue(parentId, out var children) || children.IsEmpty)
        {
            return [];
        }

        var results = new List<TTimeJob>(children.Count);

        foreach (var childId in children.Keys)
        {
            if (!_TimeJobs.TryGetValue(childId, out var child))
            {
                continue;
            }

            var clonedChild = _CloneTicker(child);
            clonedChild.Children = _BuildChildrenHierarchy(child.Id);
            results.Add(clonedChild);
        }

        return results;
    }

    // Matches EF Core's MappingExtensions.ForQueueTimeJobs but uses an in-memory children index
    private TimeJobEntity _ForQueueTimeJobs(TTimeJob job)
    {
        var root = new TimeJobEntity
        {
            Id = job.Id,
            Function = job.Function,
            Retries = job.Retries,
            RetryIntervals = job.RetryIntervals,
            UpdatedAt = job.UpdatedAt,
            ParentId = job.ParentId,
            ExecutionTime = job.ExecutionTime,
            Children = new List<TimeJobEntity>(),
        };

        if (_ChildrenIndex.TryGetValue(job.Id, out var directChildren) && !directChildren.IsEmpty)
        {
            // Pre-size children collection to avoid repeated growth
            var children = new List<TimeJobEntity>(directChildren.Count);

            foreach (var childId in directChildren.Keys)
            {
                if (!_TimeJobs.TryGetValue(childId, out var ch))
                {
                    continue;
                }

                // Only children with null ExecutionTime, matching EF mapping
                if (ch.ExecutionTime != null)
                {
                    continue;
                }

                var childEntity = new TimeJobEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    RunCondition = ch.RunCondition,
                    Children = new List<TimeJobEntity>(),
                };

                if (_ChildrenIndex.TryGetValue(ch.Id, out var grandChildren) && !grandChildren.IsEmpty)
                {
                    // Pre-size grandchildren collection
                    var grandChildList = new List<TimeJobEntity>(grandChildren.Count);

                    foreach (var grandChildId in grandChildren.Keys)
                    {
                        if (!_TimeJobs.TryGetValue(grandChildId, out var gch))
                        {
                            continue;
                        }

                        grandChildList.Add(
                            new TimeJobEntity
                            {
                                Id = gch.Id,
                                Function = gch.Function,
                                Retries = gch.Retries,
                                RetryIntervals = gch.RetryIntervals,
                                RunCondition = gch.RunCondition,
                            }
                        );
                    }

                    childEntity.Children = grandChildList;
                }

                children.Add(childEntity);
            }

            root.Children = children;
        }

        return root;
    }

    private void _AddChildIndex(Guid parentId, Guid childId)
    {
        var children = _ChildrenIndex.GetOrAdd(parentId, static _ => new ConcurrentDictionary<Guid, byte>());
        children.TryAdd(childId, 0);
    }

    private void _RemoveChildIndex(Guid parentId, Guid childId)
    {
        if (!_ChildrenIndex.TryGetValue(parentId, out var children))
        {
            return;
        }

        children.TryRemove(childId, out _);

        // Optional: cleanup empty buckets
        if (children.IsEmpty)
        {
            _ChildrenIndex.TryRemove(parentId, out _);
        }
    }

    private Guid[] _GetChildrenIds(Guid parentId)
    {
        if (!_ChildrenIndex.TryGetValue(parentId, out var children))
        {
            return [];
        }

        return children.Keys.ToArray();
    }

    private bool _CanAcquire(TTimeJob job)
    {
        // Match EF provider logic: WhereCanAcquire
        // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
        return ((job.Status == JobStatus.Idle || job.Status == JobStatus.Queued) && job.LockHolder == _lockHolder)
            || ((job.Status == JobStatus.Idle || job.Status == JobStatus.Queued) && job.LockedAt == null);
    }

    private bool _CanAcquireCronOccurrence(CronJobOccurrenceEntity<TCronJob> occurrence)
    {
        // Match EF provider logic: WhereCanAcquire
        // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
        return (
                (occurrence.Status == JobStatus.Idle || occurrence.Status == JobStatus.Queued)
                && occurrence.LockHolder == _lockHolder
            )
            || (
                (occurrence.Status == JobStatus.Idle || occurrence.Status == JobStatus.Queued)
                && occurrence.LockedAt == null
            );
    }

    private static TTimeJob _CloneTicker(TTimeJob job)
    {
        var cloned = new TTimeJob
        {
            Id = job.Id,
            Function = job.Function,
            Status = job.Status,
            Retries = job.Retries,
            RetryCount = job.RetryCount,
            ExecutionTime = job.ExecutionTime,
            InitIdentifier = job.InitIdentifier,
            LockHolder = job.LockHolder,
            LockedAt = job.LockedAt,
            ParentId = job.ParentId,
            Request = job.Request,
            ExceptionMessage = job.ExceptionMessage,
            SkippedReason = job.SkippedReason,
            ElapsedTime = job.ElapsedTime,
            RetryIntervals = job.RetryIntervals,
            RunCondition = job.RunCondition,
            ExecutedAt = job.ExecutedAt,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            Description = job.Description,
            Children = new List<TTimeJob>(),
        };

        return cloned;
    }

    private static CronJobOccurrenceEntity<TCronJob> _CloneCronOccurrence(CronJobOccurrenceEntity<TCronJob> occurrence)
    {
        return new CronJobOccurrenceEntity<TCronJob>
        {
            Id = occurrence.Id,
            CronJob = occurrence.CronJob,
            CronJobId = occurrence.CronJobId,
            Status = occurrence.Status,
            RetryCount = occurrence.RetryCount,
            ExecutionTime = occurrence.ExecutionTime,
            LockHolder = occurrence.LockHolder,
            LockedAt = occurrence.LockedAt,
            ExceptionMessage = occurrence.ExceptionMessage,
            SkippedReason = occurrence.SkippedReason,
            ElapsedTime = occurrence.ElapsedTime,
            ExecutedAt = occurrence.ExecutedAt,
            CreatedAt = occurrence.CreatedAt,
            UpdatedAt = occurrence.UpdatedAt,
        };
    }

    private void _ApplyFunctionContextToTicker(TTimeJob job, InternalFunctionContext context)
    {
        var propsToUpdate = context.GetPropsToUpdate();

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) && context.Status != JobStatus.Skipped)
        {
            job.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
        {
            job.Status = context.Status;
            job.SkippedReason = context.ExceptionDetails;
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
        {
            job.ExecutedAt = context.ExecutedAt;
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails))
            && context.Status != JobStatus.Skipped
        )
        {
            job.ExceptionMessage = context.ExceptionDetails;
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
        {
            job.ElapsedTime = context.ElapsedTime;
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
        {
            job.RetryCount = context.RetryCount;
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            job.LockHolder = null;
            job.LockedAt = null;
        }

        // UPDATED_AT ALWAYS
        job.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
    }

    private void _ApplyFunctionContextToCronOccurrence(
        CronJobOccurrenceEntity<TCronJob> occurrence,
        InternalFunctionContext context
    )
    {
        var propsToUpdate = context.GetPropsToUpdate();

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) && context.Status != JobStatus.Skipped)
        {
            occurrence.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
        {
            occurrence.Status = context.Status;
            occurrence.SkippedReason = context.ExceptionDetails;
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
        {
            occurrence.ExecutedAt = context.ExecutedAt;
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails))
            && context.Status != JobStatus.Skipped
        )
        {
            occurrence.ExceptionMessage = context.ExceptionDetails;
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
        {
            occurrence.ElapsedTime = context.ElapsedTime;
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
        {
            occurrence.RetryCount = context.RetryCount;
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            occurrence.LockHolder = null;
            occurrence.LockedAt = null;
        }

        // UPDATED_AT ALWAYS
        occurrence.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
    }

    #endregion
}
