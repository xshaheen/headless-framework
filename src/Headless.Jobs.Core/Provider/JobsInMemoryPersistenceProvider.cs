// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Provider;

internal sealed class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob> : IJobPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly ConcurrentDictionary<Guid, TTimeJob> _timeJobs = new();

    // Index of parent -> child ids for fast hierarchy lookup in memory
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _childrenIndex = new();

    private readonly ConcurrentDictionary<Guid, TCronJob> _cronJobs = new();

    private readonly ConcurrentDictionary<Guid, CronJobOccurrenceEntity<TCronJob>> _cronOccurrences = new();

    private readonly TimeProvider _timeProvider;
    private readonly string _ownerId;
    private readonly TimeSpan _leaseDuration;

    public JobsInMemoryPersistenceProvider(IServiceProvider serviceProvider)
    {
        _timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
        _ownerId = optionsBuilder?.NodeId ?? Environment.MachineName;
        _leaseDuration = optionsBuilder?.LeaseDuration ?? TimeSpan.FromMinutes(5);
    }

    // The #5 completion/claim fence (mirror of EF WhereOwnedBy): a row is touchable only when this node owns it and it
    // is still non-terminal. Extracted (#467) so the predicate that guards every completion/claim path lives in one
    // place — it was inlined 4× and a single typo would silently let one path clobber a swept/reclaimed row.
    private bool _IsOwnedNonTerminal(string? ownerId, JobStatus status)
    {
        return string.Equals(ownerId, _ownerId, StringComparison.Ordinal)
            && status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress;
    }

    // Renewal slides a RUNNING lease only (mirror of the EF RenewTimeJobLeaseAsync InProgress fence): extending an
    // Idle/Queued row would read as "lease held" and suppress cancel-on-loss. Extracted (#467) — inlined 2×.
    private bool _IsOwnedRunning(string? ownerId, JobStatus status)
    {
        return string.Equals(ownerId, _ownerId, StringComparison.Ordinal) && status is JobStatus.InProgress;
    }

    #region Time Job Methods

    public async IAsyncEnumerable<TimeJobEntity> QueueTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var timeJob in timeJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_timeJobs.TryGetValue(timeJob.Id, out var existingTicker))
            {
                // Check if we can update (similar to optimistic concurrency)
                if (existingTicker.UpdatedAt == timeJob.UpdatedAt)
                {
                    // Update the job
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.OwnerId = _ownerId;
                    updatedTicker.LockedUntil = now.Add(_leaseDuration);
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = JobStatus.Queued;

                    if (_timeJobs.TryUpdate(timeJob.Id, updatedTicker, existingTicker))
                    {
                        _ClaimIdleDescendants(timeJob.Id, now);
                        timeJob.UpdatedAt = now;
                        timeJob.OwnerId = _ownerId;
                        timeJob.LockedUntil = now.Add(_leaseDuration);
                        timeJob.Status = JobStatus.Queued;

                        yield return timeJob;
                    }
                }
            }
        }
    }

    private void _ClaimIdleDescendants(Guid rootId, DateTime now)
    {
        foreach (var childId in _GetChildrenIds(rootId))
        {
            _ClaimIdleJob(childId, now);

            foreach (var grandChildId in _GetChildrenIds(childId))
            {
                _ClaimIdleJob(grandChildId, now);
            }
        }
    }

    private void _ClaimIdleJob(Guid jobId, DateTime now)
    {
        while (_timeJobs.TryGetValue(jobId, out var existing) && existing.Status == JobStatus.Idle)
        {
            var claimed = _CloneTicker(existing);
            claimed.OwnerId = _ownerId;
            claimed.LockedUntil = now.Add(_leaseDuration);
            claimed.UpdatedAt = now;

            if (_timeJobs.TryUpdate(jobId, claimed, existing))
            {
                return;
            }
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        // First, get the time jobs that need to be updated (matching EF query)
        // NOTE: we project to the raw job here and only build the full
        //       TimeJobEntity graph after we successfully acquire the lock.
        var timeJobsToUpdate = _timeJobs
            .Values.Where(x =>
                x.ExecutionTime != null
                && _CanFallbackClaim(x.Status, x.LockedUntil, now)
                && x.ExecutionTime <= fallbackThreshold
            ) // Only tasks older than 1 second
            .ToArray();

        foreach (var job in timeJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Now update the actual job in storage
            if (_timeJobs.TryGetValue(job.Id, out var existingTicker))
            {
                // Check if we can update (matching EF's Where condition)
                if (
                    existingTicker.UpdatedAt <= job.UpdatedAt
                    && _CanFallbackClaim(existingTicker.Status, existingTicker.LockedUntil, now)
                )
                {
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.OwnerId = _ownerId;
                    updatedTicker.LockedUntil = now.Add(_leaseDuration);
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = JobStatus.Queued;

                    if (_timeJobs.TryUpdate(job.Id, updatedTicker, existingTicker))
                    {
                        _ClaimIdleDescendants(job.Id, now);

                        // Only build the full hierarchy for successfully acquired jobs
                        yield return _ForQueueTimeJobs(updatedTicker);
                    }
                }
            }
        }
    }

    public Task ReleaseAcquiredTimeJobsAsync(Guid[] timeJobIds, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var idsToRelease = timeJobIds.Length == 0 ? [.. _timeJobs.Keys] : timeJobIds;

        foreach (var id in idsToRelease)
        {
            if (_timeJobs.TryGetValue(id, out var job))
            {
                // Check if we can release (similar to WhereCanAcquire)
                if (_CanAcquire(job))
                {
                    var updatedTicker = _CloneTicker(job);
                    updatedTicker.OwnerId = null;
                    updatedTicker.LockedUntil = null;
                    updatedTicker.Status = JobStatus.Idle;
                    updatedTicker.UpdatedAt = now;

                    _timeJobs.TryUpdate(id, updatedTicker, job);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<TimeJobEntity[]> GetEarliestTimeJobsAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var oneSecondAgo = now.AddSeconds(-1);

        // Base query: same filter as EF provider, but over the snapshot
        var baseQuery = _timeJobs
            .Values.Where(x => x.ExecutionTime != null && _CanAcquire(x) && x.ExecutionTime >= oneSecondAgo)
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

    public Task<int> UpdateTimeJobAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        if (_timeJobs.TryGetValue(functionContext.JobId, out var job))
        {
            // #5 completion fence (mirror EF WhereOwnedBy): only the still-owning node may complete a
            // non-terminal row, so a swept/reclaimed row is not clobbered by a late completion.
            var ownedNonTerminal = _IsOwnedNonTerminal(job.OwnerId, job.Status);

            if (!ownedNonTerminal)
            {
                return Task.FromResult(0);
            }

            var updatedTicker = _CloneTicker(job);
            _ApplyFunctionContextToTicker(updatedTicker, functionContext);

            if (_timeJobs.TryUpdate(functionContext.JobId, updatedTicker, job))
            {
                return Task.FromResult(1);
            }
        }

        return Task.FromResult(0);
    }

    public Task<byte[]> GetTimeJobRequestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_timeJobs.TryGetValue(id, out var job))
        {
            return Task.FromResult(job.Request ?? []);
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<Guid[]> UpdateTimeJobsWithUnifiedContextAsync(
        Guid[] timeJobIds,
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        var updatedIds = new List<Guid>(timeJobIds.Length);

        foreach (var id in timeJobIds)
        {
            if (_timeJobs.TryGetValue(id, out var job))
            {
                // #316/U5 claim→start ownership recheck: reject another owner and require Queued for the
                // InProgress transition so a duplicate same-owner scheduler wrapper cannot revalidate a running row.
                var ownedNonTerminal = _IsOwnedNonTerminal(job.OwnerId, job.Status);
                var canTransitionToInProgress =
                    functionContext.Status != JobStatus.InProgress || job.Status == JobStatus.Queued;

                if (!ownedNonTerminal || !canTransitionToInProgress)
                {
                    continue;
                }

                var updatedTicker = _CloneTicker(job);
                _ApplyFunctionContextToTicker(updatedTicker, functionContext);

                if (_timeJobs.TryUpdate(id, updatedTicker, job))
                {
                    updatedIds.Add(id);
                }
            }
        }

        return Task.FromResult(updatedIds.ToArray());
    }

    public Task<TimeJobEntity[]> AcquireImmediateTimeJobsAsync(
        Guid[]? ids,
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

            if (!_timeJobs.TryGetValue(id, out var job))
            {
                continue;
            }

            if (!_CanAcquire(job))
            {
                continue;
            }

            var updatedTicker = _CloneTicker(job);
            updatedTicker.OwnerId = _ownerId;
            updatedTicker.LockedUntil = now.Add(_leaseDuration);
            updatedTicker.Status = JobStatus.InProgress;
            updatedTicker.UpdatedAt = now;

            if (_timeJobs.TryUpdate(id, updatedTicker, job))
            {
                acquired.Add(_ForQueueTimeJobs(updatedTicker));
            }
        }

        return Task.FromResult(acquired.ToArray());
    }

    public Task<int> RenewTimeJobLeaseAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // #316 sliding lease (mirror EF RenewTimeJobLeaseAsync): slide LockedUntil forward, fenced on the #5
        // completion-fence shape (still owned + non-terminal). A lost/reclaimed/terminalized row returns 0 ->
        // cancel-on-loss (U2/KTD3).
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (_timeJobs.TryGetValue(jobId, out var job))
        {
            // Renewal slides a RUNNING lease only: extending an Idle/Queued row would return 1 ("lease held") and
            // suppress cancel-on-loss. Mirror the EF RenewTimeJobLeaseAsync InProgress fence.
            var ownedRunning = _IsOwnedRunning(job.OwnerId, job.Status);

            if (!ownedRunning)
            {
                return Task.FromResult(0);
            }

            var updatedTicker = _CloneTicker(job);
            updatedTicker.LockedUntil = now.Add(_leaseDuration);
            updatedTicker.UpdatedAt = now;

            if (_timeJobs.TryUpdate(jobId, updatedTicker, job))
            {
                return Task.FromResult(1);
            }
        }

        return Task.FromResult(0);
    }

    public Task<int> ReclaimStalledTimeJobsAsync(CancellationToken cancellationToken = default)
    {
        // #316/U3 (mirror EF ReclaimStalledTimeJobsAsync): reclaim InProgress rows whose lease lapsed on ANY node, per
        // OnNodeDeath. Not owner-scoped — the trigger is a stalled lease, not a declared node death. A healthy
        // renewing job keeps a future LockedUntil and never matches.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var affected = 0;

        bool tryApply(Guid id, Action<TTimeJob> mutate)
        {
            if (!_timeJobs.TryGetValue(id, out var current))
            {
                return false;
            }

            var updated = _CloneTicker(current);
            mutate(updated);
            updated.UpdatedAt = now;
            return _timeJobs.TryUpdate(id, updated, current);
        }

        var stalled = _timeJobs.Values.Where(x => x.Status == JobStatus.InProgress && x.LockedUntil <= now).ToArray();

        foreach (var job in stalled)
        {
            switch (job.OnNodeDeath)
            {
                case NodeDeathPolicy.Retry
                    when tryApply(
                        job.Id,
                        t =>
                        {
                            t.OwnerId = null;
                            t.LockedUntil = null;
                            t.Status = JobStatus.Idle;
                        }
                    ):
                    affected++;
                    break;
                case NodeDeathPolicy.MarkFailed
                    when tryApply(
                        job.Id,
                        t =>
                        {
                            t.Status = JobStatus.Failed;
                            t.LockedUntil = null;
                            t.ExceptionMessage = "Lease lapsed while running!";
                            t.ExecutedAt = now;
                        }
                    ):
                    affected++;
                    break;
                case NodeDeathPolicy.Skip
                    when tryApply(
                        job.Id,
                        t =>
                        {
                            t.Status = JobStatus.Skipped;
                            t.LockedUntil = null;
                            t.SkippedReason = "Lease lapsed while running!";
                            t.ExecutedAt = now;
                        }
                    ):
                    affected++;
                    break;
            }
        }

        return Task.FromResult(affected);
    }

    public Task<TTimeJob?> GetTimeJobByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_timeJobs.TryGetValue(id, out var job))
        {
            var result = _BuildTickerHierarchy(job);
            return Task.FromResult<TTimeJob?>(result);
        }

        return Task.FromResult<TTimeJob?>(null);
    }

    public Task<TTimeJob[]> GetTimeJobsAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _timeJobs.Values.AsEnumerable();

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

    public Task<PaginationResult<TTimeJob>> GetTimeJobsPaginatedAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _timeJobs.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        // Match EF Core - only count and paginate root items
        query = query.Where(x => x.ParentId == null);

        // Materialize once: totalCount and the page must derive from a single snapshot (Values is
        // re-snapshotted on each enumeration) and the compiled predicate should run only once.
        var materialized = query.ToArray();
        var totalCount = materialized.Length;

        var items = materialized
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

    public Task<int> AddTimeJobsAsync(TTimeJob[] jobs, CancellationToken cancellationToken = default)
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
        if (_timeJobs.TryAdd(job.Id, job))
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

    public Task<int> UpdateTimeJobsAsync(TTimeJob[] jobs, CancellationToken cancellationToken = default)
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
        if (_timeJobs.TryGetValue(job.Id, out var existing))
        {
            if (_timeJobs.TryUpdate(job.Id, job, existing))
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

    public Task<int> RemoveTimeJobsAsync(Guid[] jobIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in jobIds)
        {
            // Remove job and all its children (cascade delete)
            if (_timeJobs.TryRemove(id, out var removed))
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
                    if (_timeJobs.TryRemove(childId, out var child))
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

    public Task<int> ReleaseDeadNodeTimeJobResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var affected = 0;

        bool tryApply(Guid id, Action<TTimeJob> mutate)
        {
            if (!_timeJobs.TryGetValue(id, out var current))
            {
                return false;
            }

            var updated = _CloneTicker(current);
            mutate(updated);
            updated.UpdatedAt = now;
            return _timeJobs.TryUpdate(id, updated, current);
        }

        // Per-policy dead-node transition (#315, #316/U4) — mirrors EF ReleaseDeadNodeTimeJobResourcesAsync. Idle/Queued
        // reclaimed immediately; InProgress arms defer to the lease (LockedUntil <= now) so a still-leased running
        // job survives a membership blip and is recovered by U3 once its lease lapses.
        var owned = _timeJobs
            .Values.Where(x => string.Equals(x.OwnerId, instanceIdentifier, StringComparison.Ordinal))
            .ToArray();

        foreach (var job in owned)
        {
            var inProgressLapsed = job.Status == JobStatus.InProgress && job.LockedUntil <= now;

            var release =
                job.Status is JobStatus.Idle or JobStatus.Queued
                || (inProgressLapsed && job.OnNodeDeath == NodeDeathPolicy.Retry);

            if (release)
            {
                if (
                    tryApply(
                        job.Id,
                        t =>
                        {
                            t.OwnerId = null;
                            t.LockedUntil = null;
                            t.Status = JobStatus.Idle;
                        }
                    )
                )
                {
                    affected++;
                }
            }
            else if (inProgressLapsed && job.OnNodeDeath == NodeDeathPolicy.MarkFailed)
            {
                if (
                    tryApply(
                        job.Id,
                        t =>
                        {
                            t.Status = JobStatus.Failed;
                            t.LockedUntil = null;
                            t.ExceptionMessage = "Node is not alive!";
                            t.ExecutedAt = now;
                        }
                    )
                )
                {
                    affected++;
                }
            }
            else if (inProgressLapsed && job.OnNodeDeath == NodeDeathPolicy.Skip)
            {
                if (
                    tryApply(
                        job.Id,
                        t =>
                        {
                            t.Status = JobStatus.Skipped;
                            t.LockedUntil = null;
                            t.SkippedReason = "Node is not alive!";
                            t.ExecutedAt = now;
                        }
                    )
                )
                {
                    affected++;
                }
            }
        }

        return Task.FromResult(affected);
    }

    #endregion

    #region Cron Job Methods

    public Task MigrateDefinedCronJobsAsync(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var (function, expression) in cronJobs)
        {
            // Deterministic id keyed by function (matches the durable provider's seed identity): a re-seed — including
            // a changed expression — updates the same row in place rather than inserting a duplicate. Single-process
            // provider, so there is no cross-node race here.
            var id = JobsSeedId.ForCronSeed(function);

            if (_cronJobs.TryGetValue(id, out var existing))
            {
                if (!string.Equals(existing.Expression, expression, StringComparison.Ordinal))
                {
                    existing.Expression = expression;
                    existing.UpdatedAt = now;
                }

                continue;
            }

            var cronJob = new TCronJob
            {
                Id = id,
                Function = function,
                Expression = expression,
                InitIdentifier = $"MemoryTicker_Seeded_{function}",
                CreatedAt = now,
                UpdatedAt = now,
                Request = [],
            };

            _cronJobs.TryAdd(id, cronJob);
        }

        return Task.CompletedTask;
    }

    public Task<CronJobEntity[]> GetAllCronJobExpressionsAsync(CancellationToken cancellationToken = default)
    {
        var result = _cronJobs.Values.Cast<CronJobEntity>().ToArray();

        return Task.FromResult(result);
    }

    public Task<TCronJob?> GetCronJobByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _cronJobs.TryGetValue(id, out var job);

        return Task.FromResult(job);
    }

    public Task<TCronJob[]> GetCronJobsAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _cronJobs.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var results = query.OrderByDescending(x => x.CreatedAt).ToArray();

        return Task.FromResult(results);
    }

    public Task<PaginationResult<TCronJob>> GetCronJobsPaginatedAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _cronJobs.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        // Materialize once so totalCount and the page share one snapshot and the predicate runs only once.
        var materialized = query.ToArray();
        var totalCount = materialized.Length;

        var items = materialized
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

    public Task<int> InsertCronJobsAsync(TCronJob[] jobs, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var job in jobs)
        {
            if (_cronJobs.TryAdd(job.Id, job))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> UpdateCronJobsAsync(TCronJob[] cronJob, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var job in cronJob)
        {
            if (_cronJobs.TryGetValue(job.Id, out var existing))
            {
                if (_cronJobs.TryUpdate(job.Id, job, existing))
                {
                    count++;
                }
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> RemoveCronJobsAsync(Guid[] cronJobIds, CancellationToken cancellationToken = default)
    {
        var count = cronJobIds.Count(id => _cronJobs.TryRemove(id, out _));

        return Task.FromResult(count);
    }

    #endregion

    #region Cron Occurrence Methods

    public Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrenceAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var mainSchedulerThreshold = now.AddSeconds(-1); // Main scheduler handles items within the 1-second window

        var query = _cronOccurrences.Values.AsEnumerable();

        if (ids is { Length: > 0 })
        {
            query = query.Where(x => ids.Contains(x.CronJobId));
        }

        var occurrence = query
            // Only recent/upcoming tasks (not heavily overdue)
            .Where(x => _CanAcquireCronOccurrence(x) && x.ExecutionTime >= mainSchedulerThreshold)
            .OrderBy(x => x.ExecutionTime)
            .FirstOrDefault();

        return Task.FromResult(occurrence!);
    }

    // KTD7: cron-occurrence creation is intentionally NOT guarded by a coarse 'jobs.cron-occurrence-creation'
    // distributed lock. The durable provider deduplicates first creation by (ExecutionTime, CronJobId) and requeues
    // existing occurrences by id; storage-level dedup is the correctness boundary. A coarse lock would add no
    // correctness and only serialize independent occurrences. Revisit only if evidence shows storage dedup is
    // insufficient (plan #267 follow-up).
    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
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
            if (_cronOccurrences.TryGetValue(occurrenceId, out var existingOccurrence))
            {
                // Update existing occurrence (should be rare - only if re-queuing)
                var updatedOccurrence = _CloneCronOccurrence(existingOccurrence);
                updatedOccurrence.OwnerId = _ownerId;
                updatedOccurrence.LockedUntil = now.Add(_leaseDuration);
                updatedOccurrence.UpdatedAt = now;
                updatedOccurrence.Status = JobStatus.Queued;
                // #464: re-stamp the policy from the cron def (context) so EF and in-memory agree on re-queue.
                updatedOccurrence.OnNodeDeath = context.OnNodeDeath;

                if (_cronOccurrences.TryUpdate(occurrenceId, updatedOccurrence, existingOccurrence))
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
                    OwnerId = _ownerId,
                    LockedUntil = now.Add(_leaseDuration),
                    // Death policy comes from the JobManagerDispatchContext (canonical, sourced from the cron def via
                    // _EarliestCronJobGroup) — set unconditionally so a MarkFailed/Skip cron never degrades to the
                    // Retry enum default when the cron row is absent from _cronJobs. Mirrors the EF QueueCronJobOccurrencesAsync
                    // projection, which always stamps item.OnNodeDeath.
                    OnNodeDeath = context.OnNodeDeath,
                    CreatedAt = context.NextCronOccurrence?.CreatedAt ?? now,
                    UpdatedAt = now,
                    RetryCount = 0,
                };

                // Attach the cron navigation when the definition is in the in-memory map (execution needs Function).
                if (_cronJobs.TryGetValue(context.Id, out var cronJob))
                {
                    newOccurrence.CronJob = cronJob;
                }

                if (_cronOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                {
                    yield return newOccurrence;
                }
            }
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueTimedOutCronJobOccurrencesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        var occurrencesToUpdate = _cronOccurrences
            .Values.Where(x => _CanFallbackClaim(x.Status, x.LockedUntil, now) && x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .ToArray();

        foreach (var occurrence in occurrencesToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_cronOccurrences.TryGetValue(occurrence.Id, out var existingOccurrence))
            {
                if (
                    existingOccurrence.UpdatedAt <= occurrence.UpdatedAt
                    && _CanFallbackClaim(existingOccurrence.Status, existingOccurrence.LockedUntil, now)
                )
                {
                    var updatedOccurrence = _CloneCronOccurrence(existingOccurrence);
                    updatedOccurrence.OwnerId = _ownerId;
                    updatedOccurrence.LockedUntil = now.Add(_leaseDuration);
                    updatedOccurrence.UpdatedAt = now;
                    updatedOccurrence.Status = JobStatus.Queued;

                    if (_cronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, existingOccurrence))
                    {
                        yield return updatedOccurrence;
                    }
                }
            }
        }
    }

    public Task<int> UpdateCronJobOccurrenceAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        if (_cronOccurrences.TryGetValue(functionContext.JobId, out var occurrence))
        {
            // #5 completion fence (mirror EF WhereOwnedBy): only the still-owning node may complete a non-terminal occurrence.
            var ownedNonTerminal = _IsOwnedNonTerminal(occurrence.OwnerId, occurrence.Status);

            if (ownedNonTerminal)
            {
                var updatedOccurrence = _CloneCronOccurrence(occurrence);
                _ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);

                // Return 1 only when the completion was actually applied (mirror EF affected-row count).
                if (_cronOccurrences.TryUpdate(functionContext.JobId, updatedOccurrence, occurrence))
                {
                    return Task.FromResult(1);
                }
            }
        }

        return Task.FromResult(0);
    }

    public Task<int> RenewCronJobOccurrenceLeaseAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
    {
        // #316 sliding lease (mirror EF RenewCronJobOccurrenceLeaseAsync). Lost/reclaimed/terminalized -> 0.
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (_cronOccurrences.TryGetValue(occurrenceId, out var occurrence))
        {
            // Renewal slides a RUNNING lease only (see RenewTimeJobLeaseAsync InProgress fence).
            var ownedRunning = _IsOwnedRunning(occurrence.OwnerId, occurrence.Status);

            if (!ownedRunning)
            {
                return Task.FromResult(0);
            }

            var updatedOccurrence = _CloneCronOccurrence(occurrence);
            updatedOccurrence.LockedUntil = now.Add(_leaseDuration);
            updatedOccurrence.UpdatedAt = now;

            if (_cronOccurrences.TryUpdate(occurrenceId, updatedOccurrence, occurrence))
            {
                return Task.FromResult(1);
            }
        }

        return Task.FromResult(0);
    }

    public Task<int> ReclaimStalledCronJobOccurrencesAsync(CancellationToken cancellationToken = default)
    {
        // #316/U3 — cron mirror of ReclaimStalledTimeJobsAsync.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var affected = 0;

        bool tryApply(Guid id, Action<CronJobOccurrenceEntity<TCronJob>> mutate)
        {
            if (!_cronOccurrences.TryGetValue(id, out var current))
            {
                return false;
            }

            var updated = _CloneCronOccurrence(current);
            mutate(updated);
            updated.UpdatedAt = now;
            return _cronOccurrences.TryUpdate(id, updated, current);
        }

        var stalled = _cronOccurrences
            .Values.Where(x => x.Status == JobStatus.InProgress && x.LockedUntil <= now)
            .ToArray();

        foreach (var occurrence in stalled)
        {
            switch (occurrence.OnNodeDeath)
            {
                case NodeDeathPolicy.Retry
                    when tryApply(
                        occurrence.Id,
                        t =>
                        {
                            t.OwnerId = null;
                            t.LockedUntil = null;
                            t.Status = JobStatus.Idle;
                        }
                    ):
                    affected++;
                    break;
                case NodeDeathPolicy.MarkFailed
                    when tryApply(
                        occurrence.Id,
                        t =>
                        {
                            t.Status = JobStatus.Failed;
                            t.LockedUntil = null;
                            t.ExceptionMessage = "Lease lapsed while running!";
                            t.ExecutedAt = now;
                        }
                    ):
                    affected++;
                    break;
                case NodeDeathPolicy.Skip
                    when tryApply(
                        occurrence.Id,
                        t =>
                        {
                            t.Status = JobStatus.Skipped;
                            t.LockedUntil = null;
                            t.SkippedReason = "Lease lapsed while running!";
                            t.ExecutedAt = now;
                        }
                    ):
                    affected++;
                    break;
            }
        }

        return Task.FromResult(affected);
    }

    public Task ReleaseAcquiredCronJobOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var idsToRelease = occurrenceIds.Length == 0 ? [.. _cronOccurrences.Keys] : occurrenceIds;

        foreach (var id in idsToRelease)
        {
            if (_cronOccurrences.TryGetValue(id, out var occurrence))
            {
                if (_CanAcquireCronOccurrence(occurrence))
                {
                    var updatedOccurrence = _CloneCronOccurrence(occurrence);
                    updatedOccurrence.OwnerId = null;
                    updatedOccurrence.LockedUntil = null;
                    updatedOccurrence.Status = JobStatus.Idle;
                    updatedOccurrence.UpdatedAt = now;

                    _cronOccurrences.TryUpdate(id, updatedOccurrence, occurrence);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<byte[]> GetCronJobOccurrenceRequestAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Cron job occurrences don't have their own request, get it from the cron job
        if (_cronOccurrences.TryGetValue(jobId, out var occurrence))
        {
            if (occurrence.CronJob != null)
            {
                return Task.FromResult(occurrence.CronJob.Request ?? []);
            }

            if (_cronJobs.TryGetValue(occurrence.CronJobId, out var cronJob))
            {
                return Task.FromResult(cronJob.Request ?? []);
            }
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<Guid[]> UpdateCronJobOccurrencesWithUnifiedContextAsync(
        Guid[] timeJobIds,
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        var updatedIds = new List<Guid>(timeJobIds.Length);

        foreach (var id in timeJobIds)
        {
            if (_cronOccurrences.TryGetValue(id, out var occurrence))
            {
                // #316/U5 — cron mirror of the strict claim→start ownership recheck.
                var ownedNonTerminal = _IsOwnedNonTerminal(occurrence.OwnerId, occurrence.Status);
                var canTransitionToInProgress =
                    functionContext.Status != JobStatus.InProgress || occurrence.Status == JobStatus.Queued;

                if (!ownedNonTerminal || !canTransitionToInProgress)
                {
                    continue;
                }

                var updatedOccurrence = _CloneCronOccurrence(occurrence);
                _ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);

                if (_cronOccurrences.TryUpdate(id, updatedOccurrence, occurrence))
                {
                    updatedIds.Add(id);
                }
            }
        }

        return Task.FromResult(updatedIds.ToArray());
    }

    public Task<int> ReleaseDeadNodeOccurrenceResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var affected = 0;

        bool tryApply(Guid id, Action<CronJobOccurrenceEntity<TCronJob>> mutate)
        {
            if (!_cronOccurrences.TryGetValue(id, out var current))
            {
                return false;
            }

            var updated = _CloneCronOccurrence(current);
            mutate(updated);
            updated.UpdatedAt = now;
            return _cronOccurrences.TryUpdate(id, updated, current);
        }

        // Per-policy dead-node transition (#315, #316/U4) — mirrors EF ReleaseDeadNodeOccurrenceResourcesAsync.
        // Idle/Queued reclaimed immediately; InProgress arms defer to the lease (LockedUntil <= now) so a
        // still-leased running occurrence survives a membership blip and is recovered by U3 once its lease lapses.
        var owned = _cronOccurrences
            .Values.Where(x => string.Equals(x.OwnerId, instanceIdentifier, StringComparison.Ordinal))
            .ToArray();

        foreach (var occurrence in owned)
        {
            var inProgressLapsed = occurrence.Status == JobStatus.InProgress && occurrence.LockedUntil <= now;

            var release =
                occurrence.Status is JobStatus.Idle or JobStatus.Queued
                || (inProgressLapsed && occurrence.OnNodeDeath == NodeDeathPolicy.Retry);

            if (release)
            {
                if (
                    tryApply(
                        occurrence.Id,
                        o =>
                        {
                            o.OwnerId = null;
                            o.LockedUntil = null;
                            o.Status = JobStatus.Idle;
                        }
                    )
                )
                {
                    affected++;
                }
            }
            else if (inProgressLapsed && occurrence.OnNodeDeath == NodeDeathPolicy.MarkFailed)
            {
                if (
                    tryApply(
                        occurrence.Id,
                        o =>
                        {
                            o.Status = JobStatus.Failed;
                            o.LockedUntil = null;
                            o.ExceptionMessage = "Node is not alive!";
                            o.ExecutedAt = now;
                        }
                    )
                )
                {
                    affected++;
                }
            }
            else if (inProgressLapsed && occurrence.OnNodeDeath == NodeDeathPolicy.Skip)
            {
                if (
                    tryApply(
                        occurrence.Id,
                        o =>
                        {
                            o.Status = JobStatus.Skipped;
                            o.LockedUntil = null;
                            o.SkippedReason = "Node is not alive!";
                            o.ExecutedAt = now;
                        }
                    )
                )
                {
                    affected++;
                }
            }
        }

        return Task.FromResult(affected);
    }

    public Task<CronJobOccurrenceEntity<TCronJob>[]> GetAllCronJobOccurrencesAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _cronOccurrences.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var results = query.OrderByDescending(x => x.CreatedAt).ToArray();

        return Task.FromResult(results);
    }

    public Task<CronOccurrenceStatusCount[]> GetCronOccurrenceGraphStatusCountsAsync(
        Guid cronJobId,
        DateTime today,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _cronOccurrences.Values.Where(x => x.CronJobId == cronJobId).ToArray();
        var range = CronOccurrenceGraphRangeSelector.Select(snapshot.Select(x => x.ExecutionTime), today);
        var counts = snapshot
            .Where(x => x.ExecutionTime.Date >= range.StartDate && x.ExecutionTime.Date <= range.EndDate)
            .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
            .Select(group => new CronOccurrenceStatusCount
            {
                Date = group.Key.Date,
                Status = group.Key.Status,
                Count = group.Count(),
            });

        return Task.FromResult(CronOccurrenceGraphRangeSelector.AddRangeBoundaries(counts, range));
    }

    public Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetAllCronJobOccurrencesPaginatedAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _cronOccurrences.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        // Materialize once so totalCount and the page share one snapshot and the predicate runs only once.
        var materialized = query.ToArray();
        var totalCount = materialized.Length;

        var items = materialized
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

    public Task<int> InsertCronJobOccurrencesAsync(
        CronJobOccurrenceEntity<TCronJob>[] cronJobOccurrences,
        CancellationToken cancellationToken = default
    )
    {
        var count = 0;
        foreach (var occurrence in cronJobOccurrences)
        {
            // Ensure navigation is populated for in-memory usage
            if (occurrence.CronJob == null && _cronJobs.TryGetValue(occurrence.CronJobId, out var cronJob))
            {
                occurrence.CronJob = cronJob;
            }

            if (_cronOccurrences.TryAdd(occurrence.Id, occurrence))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> RemoveCronJobOccurrencesAsync(
        Guid[] cronJobOccurrences,
        CancellationToken cancellationToken = default
    )
    {
        var count = 0;
        foreach (var id in cronJobOccurrences)
        {
            if (_cronOccurrences.TryRemove(id, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<CronJobOccurrenceEntity<TCronJob>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[]? occurrenceIds,
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

            if (!_cronOccurrences.TryGetValue(id, out var occurrence))
            {
                continue;
            }

            if (!_CanAcquireCronOccurrence(occurrence))
            {
                continue;
            }

            var updated = _CloneCronOccurrence(occurrence);
            updated.OwnerId = _ownerId;
            updated.LockedUntil = now.Add(_leaseDuration);
            updated.Status = JobStatus.InProgress;
            updated.UpdatedAt = now;

            if (_cronOccurrences.TryUpdate(id, updated, occurrence))
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
        if (!_childrenIndex.TryGetValue(parentId, out var children) || children.IsEmpty)
        {
            return [];
        }

        var results = new List<TTimeJob>(children.Count);

        foreach (var childId in children.Keys)
        {
            if (!_timeJobs.TryGetValue(childId, out var child))
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
            Status = job.Status,
            Retries = job.Retries,
            RetryCount = job.RetryCount,
            RetryIntervals = job.RetryIntervals,
            UpdatedAt = job.UpdatedAt,
            ParentId = job.ParentId,
            ExecutionTime = job.ExecutionTime,
            OwnerId = job.OwnerId,
            LockedUntil = job.LockedUntil,
            OnNodeDeath = job.OnNodeDeath,
            Children = [],
        };

        if (_childrenIndex.TryGetValue(job.Id, out var directChildren) && !directChildren.IsEmpty)
        {
            // Pre-size children collection to avoid repeated growth
            var children = new List<TimeJobEntity>(directChildren.Count);

            foreach (var childId in directChildren.Keys)
            {
                if (!_timeJobs.TryGetValue(childId, out var ch))
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
                    RetryCount = ch.RetryCount,
                    RetryIntervals = ch.RetryIntervals,
                    ParentId = ch.ParentId,
                    RunCondition = ch.RunCondition,
                    OnNodeDeath = ch.OnNodeDeath,
                    Children = [],
                };

                if (_childrenIndex.TryGetValue(ch.Id, out var grandChildren) && !grandChildren.IsEmpty)
                {
                    // Pre-size grandchildren collection
                    var grandChildList = new List<TimeJobEntity>(grandChildren.Count);

                    foreach (var grandChildId in grandChildren.Keys)
                    {
                        if (!_timeJobs.TryGetValue(grandChildId, out var gch))
                        {
                            continue;
                        }

                        grandChildList.Add(
                            new TimeJobEntity
                            {
                                Id = gch.Id,
                                Function = gch.Function,
                                Retries = gch.Retries,
                                RetryCount = gch.RetryCount,
                                RetryIntervals = gch.RetryIntervals,
                                ParentId = gch.ParentId,
                                RunCondition = gch.RunCondition,
                                OnNodeDeath = gch.OnNodeDeath,
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
        var children = _childrenIndex.GetOrAdd(parentId, static _ => new ConcurrentDictionary<Guid, byte>());
        children.TryAdd(childId, 0);
    }

    private static bool _CanFallbackClaim(JobStatus status, DateTime? lockedUntil, DateTime now)
    {
        return status == JobStatus.Idle || (status == JobStatus.Queued && (lockedUntil == null || lockedUntil <= now));
    }

    private void _RemoveChildIndex(Guid parentId, Guid childId)
    {
        if (!_childrenIndex.TryGetValue(parentId, out var children))
        {
            return;
        }

        children.TryRemove(childId, out _);

        // Optional: cleanup empty buckets
        if (children.IsEmpty)
        {
            _childrenIndex.TryRemove(parentId, out _);
        }
    }

    private Guid[] _GetChildrenIds(Guid parentId)
    {
        if (!_childrenIndex.TryGetValue(parentId, out var children))
        {
            return [];
        }

        return [.. children.Keys];
    }

    private bool _CanAcquire(TTimeJob job)
    {
        // Mirror EF WhereCanAcquire: (Status is Idle OR Queued) AND (mine OR never leased OR (lease expired AND
        // OnNodeDeath == Retry)). `now` comes from the injected TimeProvider (application clock, not a DB clock)
        // for InMemory↔SQL parity. The lease-expiry arm is gated on Retry (KTD5/#315).
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return (job.Status == JobStatus.Idle || job.Status == JobStatus.Queued)
            && (
                string.Equals(job.OwnerId, _ownerId, StringComparison.Ordinal)
                || job.LockedUntil == null
                || (job.LockedUntil <= now && job.OnNodeDeath == NodeDeathPolicy.Retry)
            );
    }

    private bool _CanAcquireCronOccurrence(CronJobOccurrenceEntity<TCronJob> occurrence)
    {
        // Mirror EF WhereCanAcquire: (Status is Idle OR Queued) AND (mine OR never leased OR (lease expired AND
        // OnNodeDeath == Retry)). The lease-expiry arm is gated on Retry (KTD5/#315).
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return (occurrence.Status == JobStatus.Idle || occurrence.Status == JobStatus.Queued)
            && (
                string.Equals(occurrence.OwnerId, _ownerId, StringComparison.Ordinal)
                || occurrence.LockedUntil == null
                || (occurrence.LockedUntil <= now && occurrence.OnNodeDeath == NodeDeathPolicy.Retry)
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
            OwnerId = job.OwnerId,
            LockedUntil = job.LockedUntil,
            OnNodeDeath = job.OnNodeDeath,
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
            Children = [],
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
            OwnerId = occurrence.OwnerId,
            LockedUntil = occurrence.LockedUntil,
            OnNodeDeath = occurrence.OnNodeDeath,
            ExceptionMessage = occurrence.ExceptionMessage,
            SkippedReason = occurrence.SkippedReason,
            ElapsedTime = occurrence.ElapsedTime,
            ExecutedAt = occurrence.ExecutedAt,
            CreatedAt = occurrence.CreatedAt,
            UpdatedAt = occurrence.UpdatedAt,
        };
    }

    private void _ApplyFunctionContextToTicker(TTimeJob job, JobExecutionState context)
    {
        var propsToUpdate = context.PropertiesToUpdate;

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(JobExecutionState.Status)) && context.Status != JobStatus.Skipped)
        {
            job.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            job.Status = context.Status;
            job.SkippedReason = context.ExceptionDetails;
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(JobExecutionState.ExecutedAt)))
        {
            job.ExecutedAt = context.ExecutedAt;
        }

        // EXCEPTION DETAILS
        if (propsToUpdate.Contains(nameof(JobExecutionState.ExceptionDetails)) && context.Status != JobStatus.Skipped)
        {
            job.ExceptionMessage = context.ExceptionDetails;
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(JobExecutionState.ElapsedTime)))
        {
            job.ElapsedTime = context.ElapsedTime;
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(JobExecutionState.RetryCount)))
        {
            job.RetryCount = context.RetryCount;
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(JobExecutionState.ReleaseLock)))
        {
            job.OwnerId = null;
            job.LockedUntil = null;
        }

        // UPDATED_AT ALWAYS
        job.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
    }

    private void _ApplyFunctionContextToCronOccurrence(
        CronJobOccurrenceEntity<TCronJob> occurrence,
        JobExecutionState context
    )
    {
        var propsToUpdate = context.PropertiesToUpdate;

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(JobExecutionState.Status)) && context.Status != JobStatus.Skipped)
        {
            occurrence.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            occurrence.Status = context.Status;
            occurrence.SkippedReason = context.ExceptionDetails;
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(JobExecutionState.ExecutedAt)))
        {
            occurrence.ExecutedAt = context.ExecutedAt;
        }

        // EXCEPTION DETAILS
        if (propsToUpdate.Contains(nameof(JobExecutionState.ExceptionDetails)) && context.Status != JobStatus.Skipped)
        {
            occurrence.ExceptionMessage = context.ExceptionDetails;
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(JobExecutionState.ElapsedTime)))
        {
            occurrence.ElapsedTime = context.ElapsedTime;
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(JobExecutionState.RetryCount)))
        {
            occurrence.RetryCount = context.RetryCount;
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(JobExecutionState.ReleaseLock)))
        {
            occurrence.OwnerId = null;
            occurrence.LockedUntil = null;
        }

        // UPDATED_AT ALWAYS
        occurrence.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
    }

    #endregion
}
