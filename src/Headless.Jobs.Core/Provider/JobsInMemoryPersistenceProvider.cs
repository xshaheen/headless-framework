// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Abstractions;
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
    private const int _MaxFallbackClaimBatchSize = 100;

    // KTD6 publication barrier: while a batch is being installed, every row is parked with this far-future synthetic
    // lease deadline (plus Status=InProgress + null owner) so EVERY claim/reclaim/reconcile predicate excludes it.
    // Fixed and large so it excludes rows regardless of the configured LeaseDuration; publication is synchronous (no
    // awaits between park and reveal), so no injected clock can advance past it mid-publish.
    private static readonly TimeSpan _PublicationBarrierLease = TimeSpan.FromDays(365);

    private readonly ConcurrentDictionary<Guid, TTimeJob> _timeJobs = new();

    // Index of parent -> child ids for fast hierarchy lookup in memory
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _childrenIndex = new();

    // E1: the incremental candidate index for the terminal-timed-child reconcile — the ids currently satisfying
    // (Status == Idle AND ExecutionTime != null AND ParentId != null AND a parent-terminal-gated RunCondition).
    // Maintained at every _timeJobs write so _ReconcileTerminalTimedChildren iterates this small set instead of
    // scanning all _timeJobs.Values each scheduler tick. The reconcile re-verifies each entry against the live row and
    // prunes stale ids, so the index only needs to stay a SUPERSET of the true candidates.
    private readonly ConcurrentDictionary<Guid, byte> _reconcileCandidates = new();

    private readonly ConcurrentDictionary<Guid, TCronJob> _cronJobs = new();

    private readonly ConcurrentDictionary<Guid, CronJobOccurrenceEntity<TCronJob>> _cronOccurrences = new();

    private readonly object[] _cronDefinitionLocks = [.. Enumerable.Range(0, 256).Select(static _ => new object())];

    private readonly TimeProvider _timeProvider;
    private readonly IGuidGenerator _guidGenerator;
    private readonly string _ownerId;
    private readonly TimeSpan _leaseDuration;

    // R12/KTD2: the maximum number of nodes on a root-to-leaf path that claim and hydration traverse (root = depth 1).
    // A timed descendant (ExecutionTime != null) is a boundary — excluded from the in-tree walk and claimed
    // independently (U5) — so the walk descends only through non-timed children to this depth.
    private readonly int _maxChainDepth;

    public JobsInMemoryPersistenceProvider(IServiceProvider serviceProvider)
    {
        _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        _guidGenerator = serviceProvider.GetRequiredService<IGuidGenerator>();
        var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
        _ownerId = optionsBuilder?.NodeId ?? Environment.MachineName;
        _leaseDuration = optionsBuilder?.LeaseDuration ?? TimeSpan.FromMinutes(5);
        _maxChainDepth = optionsBuilder?.MaxChainDepth ?? SchedulerOptionsBuilder.DefaultMaxChainDepth;
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

    // U5/KTD3 claim gate: a timed descendant (ParentId != null AND ExecutionTime != null) with a parent-terminal-gated
    // run condition is claimable only once its parent reached the MATCHING terminal state. Roots (ParentId == null),
    // non-timed children (ExecutionTime == null, walked in-tree), and InProgress/null-condition timed children stay
    // ungated. Mirrors the EF WhereClaimableUnderParentTerminalGate correlated-subquery predicate; the coherent
    // single-process map is this provider's authority (no DB clock).
    private bool _ParentGateAllowsClaim(TTimeJob job)
    {
        if (
            job.ParentId is not { } parentId
            || job.ExecutionTime is null
            || !ChainRunConditionRules.IsParentTerminalGated(job.RunCondition)
        )
        {
            return true;
        }

        return _timeJobs.TryGetValue(parentId, out var parent)
            && ChainRunConditionRules.ParentTerminalMatches(job.RunCondition, parent.Status);
    }

    // E1: the reconcile-candidate predicate — a row currently eligible for the terminal-timed-child reconcile. Mirrors
    // the first filter in _ReconcileTerminalTimedChildren exactly so the index and the reconcile agree on membership.
    private static bool _IsReconcileCandidate(TTimeJob job)
    {
        return job.Status == JobStatus.Idle
            && job.ExecutionTime is not null
            && job.ParentId is not null
            && ChainRunConditionRules.IsParentTerminalGated(job.RunCondition);
    }

    // E1: keep _reconcileCandidates in sync with a just-written row. Called after every successful _timeJobs write.
    //
    // Converge on the CURRENT live row (re-read by id after the CAS), NOT on this caller's written value. With
    // interleaved writers to the same id, syncing against a stale local value can drop a live candidate from the index
    // (an older writer's remove landing after a newer writer's add) — and the reconcile loop only visits ids already
    // in the index, so it prunes false positives but never re-adds a wrongly-removed candidate. The index must stay a
    // SUPERSET of the true candidates; a false negative permanently strands a candidate. Re-reading points every
    // syncer at live state, and the post-remove re-check re-adds the row if a concurrent writer re-made it a candidate
    // between our read and our remove — so once writes on the id quiesce, the last index operation is an add whenever
    // the live row is a candidate, and no live candidate is ever left out of the index.
    private void _SyncReconcileCandidate(TTimeJob job)
    {
        var jobId = job.Id;

        if (_timeJobs.TryGetValue(jobId, out var live) && _IsReconcileCandidate(live))
        {
            _reconcileCandidates.TryAdd(jobId, 0);
            return;
        }

        _reconcileCandidates.TryRemove(jobId, out _);

        // A concurrent writer may have re-made the row a candidate between our re-read and our remove; re-check the
        // live row and re-add so the remove can never drop a candidate that is live again.
        if (_timeJobs.TryGetValue(jobId, out var afterRemove) && _IsReconcileCandidate(afterRemove))
        {
            _reconcileCandidates.TryAdd(jobId, 0);
        }
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
                if (existingTicker.UpdatedAt == timeJob.UpdatedAt && _CanAcquire(existingTicker))
                {
                    // Update the job
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.OwnerId = _ownerId;
                    updatedTicker.LockedUntil = now.Add(_leaseDuration);
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = JobStatus.Queued;

                    if (_timeJobs.TryUpdate(timeJob.Id, updatedTicker, existingTicker))
                    {
                        _SyncReconcileCandidate(updatedTicker);
                        var claimedIds = _ClaimIdleDescendants(timeJob.Id, now);
                        timeJob.UpdatedAt = now;
                        timeJob.OwnerId = _ownerId;
                        timeJob.LockedUntil = now.Add(_leaseDuration);
                        timeJob.Status = JobStatus.Queued;

                        // KTD2: the peek-hydrated tree may include non-idle nodes (and their tails) the claim did not
                        // lease; execute strictly the claimed set so nothing runs unclaimed.
                        TimeJobSubtreeOperations.PruneToClaimedSet(timeJob, claimedIds);

                        yield return timeJob;
                    }
                }
            }
        }
    }

    private HashSet<Guid> _ClaimIdleDescendants(Guid rootId, DateTime now)
    {
        // R12/KTD2: lease the non-timed in-tree subtree down to MaxChainDepth (root is depth 1) and return the exact
        // set of claimed ids (root + descendants). A timed child is a boundary (not descended into, claimed
        // independently by U5); a non-idle child (terminalized by a sweep, or running) is ALSO a boundary — the
        // frontier stops there so a node below an unclaimable one is never leased and never executed. The caller
        // rebuilds the returned tree strictly from this set.
        var claimed = new HashSet<Guid> { rootId };
        var frontier = new List<Guid> { rootId };
        var depth = 1;

        while (frontier.Count != 0 && depth < _maxChainDepth)
        {
            var next = new List<Guid>();

            foreach (var parentId in frontier)
            {
                foreach (var childId in _GetChildrenIds(parentId))
                {
                    if (!_timeJobs.TryGetValue(childId, out var child) || child.ExecutionTime is not null)
                    {
                        continue;
                    }

                    // Only descend into children we actually leased (were idle); stop at a non-idle frontier.
                    if (_ClaimIdleJob(childId, now))
                    {
                        claimed.Add(childId);
                        next.Add(childId);
                    }
                }
            }

            frontier = next;
            depth++;
        }

        return claimed;
    }

    private bool _ClaimIdleJob(Guid jobId, DateTime now)
    {
        while (_timeJobs.TryGetValue(jobId, out var existing) && existing.Status == JobStatus.Idle)
        {
            var claimed = _CloneTicker(existing);
            claimed.OwnerId = _ownerId;
            claimed.LockedUntil = now.Add(_leaseDuration);
            claimed.UpdatedAt = now;

            if (_timeJobs.TryUpdate(jobId, claimed, existing))
            {
                _SyncReconcileCandidate(claimed);
                return true;
            }
        }

        return false;
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
                && _ParentGateAllowsClaim(x) // U5/KTD3: the fallback claims timed rows directly, so it is gated too
            ) // Only tasks older than 1 second
            .OrderBy(x => x.ExecutionTime)
            .ThenBy(x => x.Id)
            .Take(_MaxFallbackClaimBatchSize)
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
                    && _ParentGateAllowsClaim(existingTicker)
                )
                {
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.OwnerId = _ownerId;
                    updatedTicker.LockedUntil = now.Add(_leaseDuration);
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = JobStatus.Queued;

                    if (_timeJobs.TryUpdate(job.Id, updatedTicker, existingTicker))
                    {
                        _SyncReconcileCandidate(updatedTicker);
                        var claimedIds = _ClaimIdleDescendants(job.Id, now);

                        // Only build the full hierarchy for successfully acquired jobs, pruned to the claimed set
                        // (KTD2) so a non-idle node the claim stopped at — and its tail — never executes unclaimed.
                        var hydrated = _ForQueueTimeJobs(updatedTicker);
                        TimeJobSubtreeOperations.PruneToClaimedSet(hydrated, claimedIds);

                        yield return hydrated;
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

                    if (_timeJobs.TryUpdate(id, updatedTicker, job))
                    {
                        _SyncReconcileCandidate(updatedTicker);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<TimeJobEntity[]> GetEarliestTimeJobsAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var oneSecondAgo = now.AddSeconds(-1);

        // Base query: same filter as EF provider, but over the snapshot. U5/KTD3: a timed descendant surfaces here as
        // its own candidate (excluded from the in-tree walk), so the parent gate keeps it out until its parent matched.
        var baseQuery = _timeJobs
            .Values.Where(x =>
                x.ExecutionTime != null
                && _CanAcquire(x)
                && x.ExecutionTime >= oneSecondAgo
                && _ParentGateAllowsClaim(x)
            )
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
                _SyncReconcileCandidate(updatedTicker);
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

    public Task<bool> RequestTimeJobCancellationAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            if (!_timeJobs.TryGetValue(jobId, out var job))
            {
                return Task.FromResult(false);
            }

            if (job.CancelRequested || job.Status is not (JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress))
            {
                return Task.FromResult(false);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var updated = _CloneTicker(job);
            updated.CancelRequested = true;
            updated.UpdatedAt = now;

            if (job.Status == JobStatus.Idle)
            {
                updated.Status = JobStatus.Cancelled;
                updated.ExecutedAt = now;
                updated.OwnerId = null;
                updated.LockedUntil = null;
            }

            if (!_timeJobs.TryUpdate(jobId, updated, job))
            {
                continue;
            }

            _SyncReconcileCandidate(updated);

            if (job.Status == JobStatus.Idle)
            {
                // Non-timed children keep the existing cancellation handling. U5/KTD3: the cancelled parent's TIMED
                // children are reconciled by ApplyParentTerminalRunConditionsAsync, driven post-cancellation by the
                // manager so the released-child scheduler wake is threaded through the same path as the executor/sweep
                // reconcile (and by the poll-time / sweep reconcile as a backstop).
                _ApplyCancelledParentRunConditions(jobId, now);
            }

            return Task.FromResult(true);
        }
    }

    public Task<bool?> IsTimeJobCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_timeJobs.TryGetValue(jobId, out var job) || !_IsOwnedRunning(job.OwnerId, job.Status))
        {
            return Task.FromResult<bool?>(null);
        }

        return Task.FromResult<bool?>(job.CancelRequested);
    }

    private void _ApplyCancelledParentRunConditions(Guid parentId, DateTime now)
    {
        foreach (var childId in _GetChildrenIds(parentId))
        {
            while (_timeJobs.TryGetValue(childId, out var child))
            {
                if (child.Status != JobStatus.Idle || child.ExecutionTime is not null)
                {
                    break;
                }

                if (_RunsAfterCancelled(child.RunCondition))
                {
                    var released = _CloneTicker(child);
                    released.ExecutionTime = now;
                    released.OwnerId = null;
                    released.LockedUntil = null;
                    released.UpdatedAt = now;
                    if (!_timeJobs.TryUpdate(childId, released, child))
                    {
                        continue;
                    }

                    // The re-stamped non-timed child now carries an ExecutionTime, so it enters the reconcile-candidate
                    // state (E1) — index it so the terminal-timed-child reconcile can still reach it.
                    _SyncReconcileCandidate(released);
                    break;
                }

                _SkipRejectedBranch(
                    childId,
                    now,
                    "Parent cancellation did not satisfy the job run condition.",
                    "Ancestor job was skipped after parent cancellation.",
                    requireUnscheduled: true
                );
                break;
            }
        }
    }

    // Skips an idle branch and cascades the skip to its whole subtree, returning the number of rows skipped. The node
    // uses <paramref name="reason"/>; every descendant uses <paramref name="cascadeReason"/>. When
    // <paramref name="requireUnscheduled"/> is set the node is skipped only if it has no ExecutionTime (the
    // cancellation path's non-timed filter); the cascade always skips regardless (a skipped node's whole tail dies).
    private int _SkipRejectedBranch(
        Guid jobId,
        DateTime now,
        string reason,
        string cascadeReason,
        bool requireUnscheduled = false
    )
    {
        var skippedCount = 0;

        while (_timeJobs.TryGetValue(jobId, out var job))
        {
            if (job.Status != JobStatus.Idle || (requireUnscheduled && job.ExecutionTime is not null))
            {
                return skippedCount;
            }

            var skipped = _CloneTicker(job);
            skipped.Status = JobStatus.Skipped;
            skipped.ExecutedAt = now;
            skipped.OwnerId = null;
            skipped.LockedUntil = null;
            skipped.SkippedReason = reason;
            skipped.UpdatedAt = now;
            if (_timeJobs.TryUpdate(jobId, skipped, job))
            {
                _SyncReconcileCandidate(skipped);
                skippedCount++;
                break;
            }
        }

        foreach (var childId in _GetChildrenIds(jobId))
        {
            skippedCount += _SkipRejectedBranch(childId, now, cascadeReason, cascadeReason);
        }

        return skippedCount;
    }

    private static bool _RunsAfterCancelled(RunCondition? runCondition) =>
        runCondition
            is RunCondition.OnCancelled
                or RunCondition.OnFailureOrCancelled
                or RunCondition.OnAnyCompletedStatus;

    // U5/KTD3: cascade skip-reason for descendants of a timed child whose parent's run condition did not match; the
    // direct-child mismatch reason is the shared ChainRunConditionRules.RunConditionMismatchReason.
    private const string _AncestorSkippedReason =
        "Ancestor job was skipped after its parent's run condition did not match.";

    public Task<DateTime?> ApplyParentTerminalRunConditionsAsync(
        Guid? parentId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (earliest, _) = _ReconcileTerminalTimedChildren(parentId, skipOnly: false, now);

        return Task.FromResult(earliest);
    }

    public Task<int> SkipStrandedTimedChildrenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (_, skipped) = _ReconcileTerminalTimedChildren(parentId: null, skipOnly: true, now);

        return Task.FromResult(skipped);
    }

    // The set-based release/skip reconcile (KTD3), single-process form. For every IDLE timed child whose parent has
    // reached a terminal state: a MATCHING run condition releases (re-stamping a past-due child to now so the
    // staleness-filtered main peek claims it promptly); a NON-matching one is skipped with its subtree. parentId
    // constrains to one parent (post-terminal, from the executor/cancellation); null reconciles all terminal parents
    // (the sweep follow-up). skipOnly is the poll-time safety net that never releases. Returns the earliest released
    // ExecutionTime (for RestartIfNeeded) and the number of rows skipped.
    private (DateTime? Earliest, int Skipped) _ReconcileTerminalTimedChildren(
        Guid? parentId,
        bool skipOnly,
        DateTime now
    )
    {
        DateTime? earliest = null;
        var skipped = 0;

        // E1: iterate the incremental candidate index instead of scanning all _timeJobs.Values each tick. Each entry is
        // re-verified against the live row here, so a stale id (the job left the candidate state after it was indexed)
        // is filtered and pruned — the index only needs to stay a SUPERSET of the true candidates.
        foreach (var candidateId in _reconcileCandidates.Keys)
        {
            if (
                !_timeJobs.TryGetValue(candidateId, out var child)
                || child.Status != JobStatus.Idle
                || child.ExecutionTime is null
                || child.ParentId is not { } childParentId
                || !ChainRunConditionRules.IsParentTerminalGated(child.RunCondition)
            )
            {
                _reconcileCandidates.TryRemove(candidateId, out _);
                continue;
            }

            if (parentId is { } target && childParentId != target)
            {
                continue;
            }

            if (
                !_timeJobs.TryGetValue(childParentId, out var parent)
                || !ChainRunConditionRules.IsTerminal(parent.Status)
            )
            {
                continue;
            }

            if (ChainRunConditionRules.ParentTerminalMatches(child.RunCondition, parent.Status))
            {
                if (skipOnly)
                {
                    continue; // the safety net never makes a child eligible early
                }

                var released = _ReleaseMatchingTimedChild(child.Id, now);
                if (released is { } releasedTime && (earliest is null || releasedTime < earliest))
                {
                    earliest = releasedTime;
                }
            }
            else
            {
                skipped += _SkipRejectedBranch(
                    child.Id,
                    now,
                    ChainRunConditionRules.RunConditionMismatchReason,
                    _AncestorSkippedReason
                );
            }
        }

        return (earliest, skipped);
    }

    // Releases a matching idle timed child: the gate now passes (parent terminal-matched), so it is claimable at its
    // own ExecutionTime. A future time is left untouched (it runs at its scheduled time); a past-due time is re-stamped
    // to now so the staleness-filtered main peek claims it promptly rather than the slow fallback. Returns the
    // effective (post-restamp) ExecutionTime so the scheduler can be woken for it.
    private DateTime? _ReleaseMatchingTimedChild(Guid childId, DateTime now)
    {
        while (_timeJobs.TryGetValue(childId, out var child))
        {
            if (child.Status != JobStatus.Idle || child.ExecutionTime is null)
            {
                return null;
            }

            if (child.ExecutionTime.Value > now)
            {
                return child.ExecutionTime.Value;
            }

            var released = _CloneTicker(child);
            released.ExecutionTime = now;
            released.OwnerId = null;
            released.LockedUntil = null;
            released.UpdatedAt = now;
            if (_timeJobs.TryUpdate(childId, released, child))
            {
                _SyncReconcileCandidate(released);
                return now;
            }
        }

        return null;
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
                    _SyncReconcileCandidate(updatedTicker);
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

            if (!_CanAcquire(job) || !_ParentGateAllowsClaim(job))
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
                _SyncReconcileCandidate(updatedTicker);
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
                _SyncReconcileCandidate(updatedTicker);
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
            if (!_timeJobs.TryUpdate(id, updated, current))
            {
                return false;
            }

            _SyncReconcileCandidate(updated);
            return true;
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
        // KTD6 cross-root all-or-nothing (IJobPersistenceProvider.AddTimeJobsAsync contract): the WHOLE call — every
        // root AND every descendant across ALL chains — is one atomic unit. Phase 1 flattens every subtree and validates
        // ids (unique within this call — whether in the same or another root's subtree — AND absent from stored state);
        // phase 2 commits. A collision anywhere leaves NOTHING from the call visible, so one bad root can never strand a
        // sibling root's tree (the old per-root loop committed each subtree independently and violated this).
        var flattened = new List<TTimeJob>();
        foreach (var job in jobs)
        {
            _CollectSubtree(job, parentId: null, flattened);
        }

        var seen = new HashSet<Guid>(flattened.Count);
        foreach (var node in flattened)
        {
            if (!seen.Add(node.Id) || _timeJobs.ContainsKey(node.Id))
            {
                // Duplicate id within the call, or a collision with an existing row — reject the whole call.
                return Task.FromResult(0);
            }
        }

        return Task.FromResult(_CommitValidatedSubtrees(flattened));
    }

    private int _AddTickerWithChildren(TTimeJob job, Guid? parentId = null)
    {
        // KTD6 all-or-nothing: validate the WHOLE subtree — structure and id uniqueness, both within the subtree and
        // against already-stored rows — BEFORE mutating any shared dictionary, so a collision anywhere leaves nothing
        // visible (never a partially-added parent). Flattening also stamps each node's ParentId.
        var flattened = new List<TTimeJob>();
        _CollectSubtree(job, parentId, flattened);

        var seen = new HashSet<Guid>(flattened.Count);
        foreach (var node in flattened)
        {
            if (!seen.Add(node.Id) || _timeJobs.ContainsKey(node.Id))
            {
                // Duplicate id within the subtree, or a collision with an existing row — reject the whole tree.
                return 0;
            }
        }

        return _CommitValidatedSubtrees(flattened);
    }

    // Commits a pre-validated, pre-order-flattened node set (each subtree is a root followed by its descendants) as ONE
    // reader-visible unit, via a publication barrier.
    //
    // PARK (any order): install every node under the barrier state — Status=InProgress + null owner + a far-future
    // synthetic lease — which EVERY claim/reclaim/reconcile predicate already excludes (Idle|Queued claim & fallback
    // filters, the Idle reconcile-candidacy filter, the LockedUntil<=now stalled-reclaim filter, and the owner-scoped
    // completion/renew/dead-node paths). The _childrenIndex is installed here too. Because nothing in the batch is
    // claimable while parked, a mid-install collision can safely roll the WHOLE batch back — no concurrent claimer can
    // be holding any row — closing the window where an earlier version published a row before a later cross-root
    // collision returned 0.
    //
    // REVEAL (reverse pre-order, descendants before their ancestor): swap each barrier row for the real row. Reverse
    // order guarantees that when a node becomes claimable, its non-timed in-tree children are already Idle so the
    // node's atomic claim leases them (root-first would let a concurrent root claim miss a still-parked non-timed child
    // and orphan it). A parked ancestor keeps a gated timed descendant unclaimable (parent not terminal) throughout;
    // an ungated timed descendant becomes independently claimable on its own reveal, after the batch is fully
    // committed, so no rollback can ever strip a row a claimer already took.
    private int _CommitValidatedSubtrees(List<TTimeJob> flattened)
    {
        var barrierDeadline = _timeProvider.GetUtcNow().UtcDateTime.Add(_PublicationBarrierLease);
        var barriers = new List<TTimeJob>(flattened.Count);

        for (var index = 0; index < flattened.Count; index++)
        {
            var node = flattened[index];

            var barrierRow = _CloneTicker(node);
            barrierRow.Status = JobStatus.InProgress;
            barrierRow.OwnerId = null;
            barrierRow.LockedUntil = barrierDeadline;

            if (!_timeJobs.TryAdd(node.Id, barrierRow))
            {
                // A concurrent add raced us on this id after the phase-1 pre-check; roll the whole batch back. Every
                // installed row is still parked (unclaimable), so nothing can hold it — the conditional removes win.
                for (var installed = 0; installed < barriers.Count; installed++)
                {
                    var installedNode = flattened[installed];

                    // Conditional (KeyValuePair) remove: pull the row ONLY while the map still holds the exact barrier
                    // instance we inserted. Unwind the child index only for the rows we actually removed.
                    if (_timeJobs.TryRemove(new KeyValuePair<Guid, TTimeJob>(installedNode.Id, barriers[installed])))
                    {
                        if (installedNode.ParentId.HasValue)
                        {
                            _RemoveChildIndex(installedNode.ParentId.Value, installedNode.Id);
                        }
                    }
                }

                return 0;
            }

            if (node.ParentId.HasValue)
            {
                _AddChildIndex(node.ParentId.Value, node.Id);
            }

            barriers.Add(barrierRow);
        }

        for (var index = flattened.Count - 1; index >= 0; index--)
        {
            var node = flattened[index];

            // Nothing else can mutate a parked barrier row (every predicate excludes it), so this swap always wins.
            _timeJobs.TryUpdate(node.Id, node, barriers[index]);
            _SyncReconcileCandidate(node);
        }

        return flattened.Count;
    }

    private static void _CollectSubtree(TTimeJob job, Guid? parentId, List<TTimeJob> collector)
    {
        // Set the parent ID if this is a child
        if (parentId.HasValue)
        {
            job.ParentId = parentId.Value;
        }

        collector.Add(job);

        if (job.Children is { Count: > 0 })
        {
            foreach (var child in job.Children)
            {
                // Cast to TTimeJob since Children is ICollection<TTimeJob>
                if (child is { } childTicker)
                {
                    _CollectSubtree(childTicker, job.Id, collector);
                }
            }
        }
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
                _SyncReconcileCandidate(job);

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
                _reconcileCandidates.TryRemove(id, out _);

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
                        _reconcileCandidates.TryRemove(childId, out _);
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
            if (!_timeJobs.TryUpdate(id, updated, current))
            {
                return false;
            }

            _SyncReconcileCandidate(updated);
            return true;
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
                    lock (_GetCronDefinitionLock(id))
                    {
                        if (!_cronJobs.TryGetValue(id, out var current))
                        {
                            continue;
                        }

                        var updated = _CloneCronJob(current);
                        updated.Expression = expression;
                        updated.ScheduleRevision++;
                        updated.UpdatedAt = now;

                        foreach (var pair in _cronOccurrences.Where(x => x.Value.CronJobId == id).ToArray())
                        {
                            if (pair.Value.Status is not (JobStatus.Idle or JobStatus.Queued))
                            {
                                continue;
                            }

                            var skipped = _CloneCronOccurrence(pair.Value);
                            skipped.Status = JobStatus.Skipped;
                            skipped.ExecutedAt = now;
                            skipped.UpdatedAt = now;
                            skipped.SkippedReason = "Cron definition updated";
                            skipped.OwnerId = null;
                            skipped.LockedUntil = null;
                            _cronOccurrences[pair.Key] = skipped;
                        }

                        _cronJobs[id] = updated;
                    }
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

        return Task.FromResult(job is null ? null : _CloneCronJob(job));
    }

    public Task<TCronJob?> PauseCronJobAsync(
        Guid cronJobId,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_GetCronDefinitionLock(cronJobId))
        {
            if (!_cronJobs.TryGetValue(cronJobId, out var current) || current.IsPaused)
            {
                return Task.FromResult<TCronJob?>(null);
            }

            var updated = _CloneCronJob(current);
            updated.IsPaused = true;
            updated.ScheduleRevision++;
            updated.UpdatedAt = operationTimeUtc;

            foreach (var pair in _cronOccurrences.Where(x => x.Value.CronJobId == cronJobId).ToArray())
            {
                if (pair.Value.Status is not (JobStatus.Idle or JobStatus.Queued))
                {
                    continue;
                }

                var skipped = _CloneCronOccurrence(pair.Value);
                skipped.Status = JobStatus.Skipped;
                skipped.ExecutedAt = operationTimeUtc;
                skipped.UpdatedAt = operationTimeUtc;
                skipped.SkippedReason = "Cron definition paused";
                skipped.OwnerId = null;
                skipped.LockedUntil = null;
                _cronOccurrences[pair.Key] = skipped;
            }

            _cronJobs[cronJobId] = updated;
            return Task.FromResult<TCronJob?>(_CloneCronJob(updated));
        }
    }

    public Task<TCronJob?> ResumeCronJobAsync(
        Guid cronJobId,
        long expectedScheduleRevision,
        CronJobOccurrenceEntity<TCronJob> nextOccurrence,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_GetCronDefinitionLock(cronJobId))
        {
            if (
                !_cronJobs.TryGetValue(cronJobId, out var current)
                || !current.IsPaused
                || current.ScheduleRevision != expectedScheduleRevision
                || nextOccurrence.CronJobId != cronJobId
                || _cronOccurrences.ContainsKey(nextOccurrence.Id)
            )
            {
                return Task.FromResult<TCronJob?>(null);
            }

            var updated = _CloneCronJob(current);
            updated.IsPaused = false;
            updated.ScheduleRevision++;
            updated.UpdatedAt = operationTimeUtc;

            var replacement = _CloneCronOccurrence(nextOccurrence);
            replacement.CronJob = updated;
            _cronOccurrences[nextOccurrence.Id] = replacement;
            _cronJobs[cronJobId] = updated;

            return Task.FromResult<TCronJob?>(_CloneCronJob(updated));
        }
    }

    public Task<TCronJob[]?> UpdateCronJobsAtomicallyAsync(
        CronJobAtomicUpdate<TCronJob>[] updates,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (updates.Select(x => x.Definition.Id).ToHashSet().Count != updates.Length)
        {
            return Task.FromResult<TCronJob[]?>(null);
        }

        var lockIndexes = updates
            .Select(x => _GetCronDefinitionLockIndex(x.Definition.Id))
            .Distinct()
            .Order()
            .ToArray();
        var locks = lockIndexes.Select(index => _cronDefinitionLocks[index]).ToArray();
        var acquiredLockCount = 0;

        try
        {
            foreach (var definitionLock in locks)
            {
                Monitor.Enter(definitionLock);
                acquiredLockCount++;
            }

            var prepared =
                new List<(TCronJob Definition, CronJobOccurrenceEntity<TCronJob>? Replacement, bool Changed)>();

            foreach (var update in updates)
            {
                if (
                    !_cronJobs.TryGetValue(update.Definition.Id, out var current)
                    || current.ScheduleRevision != update.ExpectedScheduleRevision
                )
                {
                    return Task.FromResult<TCronJob[]?>(null);
                }

                var changed =
                    !string.Equals(current.Expression, update.Definition.Expression, StringComparison.Ordinal)
                    || !string.Equals(current.TimeZoneId, update.Definition.TimeZoneId, StringComparison.Ordinal);

                if (changed && !current.IsPaused && update.NextOccurrence is null)
                {
                    return Task.FromResult<TCronJob[]?>(null);
                }

                var definition = _CloneCronJob(update.Definition);
                definition.IsPaused = current.IsPaused;
                definition.ScheduleRevision = changed ? current.ScheduleRevision + 1 : current.ScheduleRevision;
                definition.CreatedAt = current.CreatedAt;
                definition.UpdatedAt = operationTimeUtc;

                CronJobOccurrenceEntity<TCronJob>? replacement = null;
                if (changed && !current.IsPaused)
                {
                    replacement = _CloneCronOccurrence(update.NextOccurrence!);
                    replacement.CronJobId = definition.Id;
                    replacement.CronJob = definition;

                    if (_cronOccurrences.ContainsKey(replacement.Id))
                    {
                        return Task.FromResult<TCronJob[]?>(null);
                    }
                }

                prepared.Add((definition, replacement, changed));
            }

            foreach (var (definition, replacement, changed) in prepared)
            {
                if (changed)
                {
                    foreach (var pair in _cronOccurrences.Where(x => x.Value.CronJobId == definition.Id).ToArray())
                    {
                        if (pair.Value.Status is not (JobStatus.Idle or JobStatus.Queued))
                        {
                            continue;
                        }

                        var skipped = _CloneCronOccurrence(pair.Value);
                        skipped.Status = JobStatus.Skipped;
                        skipped.ExecutedAt = operationTimeUtc;
                        skipped.UpdatedAt = operationTimeUtc;
                        skipped.SkippedReason = "Cron definition updated";
                        skipped.OwnerId = null;
                        skipped.LockedUntil = null;
                        _cronOccurrences[pair.Key] = skipped;
                    }

                    if (replacement is not null)
                    {
                        _cronOccurrences[replacement.Id] = replacement;
                    }
                }

                _cronJobs[definition.Id] = definition;
            }

            return Task.FromResult<TCronJob[]?>([.. prepared.Select(x => _CloneCronJob(x.Definition))]);
        }
        finally
        {
            for (var index = acquiredLockCount - 1; index >= 0; index--)
            {
                Monitor.Exit(locks[index]);
            }
        }
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

            lock (_GetCronDefinitionLock(context.Id))
            {
                if (
                    !_cronJobs.TryGetValue(context.Id, out var currentDefinition)
                    || currentDefinition.IsPaused
                    || context.IsPaused
                    || currentDefinition.ScheduleRevision != context.ScheduleRevision
                )
                {
                    continue;
                }

                var liveOccurrence = _cronOccurrences.Values.FirstOrDefault(x =>
                    x.CronJobId == context.Id
                    && x.ExecutionTime == cronJobOccurrences.Key
                    && x.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress
                );
                if (liveOccurrence is not null && liveOccurrence.Id != context.NextCronOccurrence?.Id)
                {
                    continue;
                }

                // Each cron occurrence should have a unique ID
                var occurrenceId = context.NextCronOccurrence?.Id ?? _guidGenerator.Create();

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
                        CronJob = currentDefinition,
                    };

                    // Attach the cron navigation when the definition is in the in-memory map (execution needs Function).
                    if (_cronOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                    {
                        yield return newOccurrence;
                    }
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
            .OrderBy(x => x.ExecutionTime)
            .ThenBy(x => x.Id)
            .Take(_MaxFallbackClaimBatchSize)
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
                lock (_GetCronDefinitionLock(occurrence.CronJobId))
                {
                    // #316/U5 — cron mirror of the strict claim→start ownership recheck.
                    var ownedNonTerminal = _IsOwnedNonTerminal(occurrence.OwnerId, occurrence.Status);
                    var canTransitionToInProgress =
                        functionContext.Status != JobStatus.InProgress || occurrence.Status == JobStatus.Queued;
                    var definitionAllowsStart =
                        functionContext.Status != JobStatus.InProgress
                        || (_cronJobs.TryGetValue(occurrence.CronJobId, out var definition) && !definition.IsPaused);

                    if (!ownedNonTerminal || !canTransitionToInProgress || !definitionAllowsStart)
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

    // Mirrors EF Core's flat-load + AttachNonTimedDescendantsAsync hydration but uses an in-memory children index.
    // R12/KTD2: hydrate the non-timed in-tree subtree down to MaxChainDepth (root = depth 1), carrying the full field
    // set at every level (dropping RetryCount from any level silently resets the retry budget after restart —
    // docs/solutions precedent). Timed descendants (ExecutionTime != null) stay excluded — U5 claims them independently.
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
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            ParentId = job.ParentId,
            ExecutionTime = job.ExecutionTime,
            OwnerId = job.OwnerId,
            LockedUntil = job.LockedUntil,
            OnNodeDeath = job.OnNodeDeath,
            Children = [],
        };

        _AttachNonTimedChildren(root, job.Id, depth: 1);

        return root;
    }

    private void _AttachNonTimedChildren(TimeJobEntity parentEntity, Guid parentId, int depth)
    {
        // A node at MaxChainDepth is the deepest in-tree node; do not load its children.
        if (depth >= _maxChainDepth)
        {
            return;
        }

        if (!_childrenIndex.TryGetValue(parentId, out var directChildren) || directChildren.IsEmpty)
        {
            return;
        }

        var children = new List<TimeJobEntity>(directChildren.Count);

        foreach (var childId in directChildren.Keys)
        {
            // Only children with null ExecutionTime, matching the EF mapping (timed descendants run via U5's gate).
            if (!_timeJobs.TryGetValue(childId, out var ch) || ch.ExecutionTime is not null)
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
                CreatedAt = ch.CreatedAt,
                UpdatedAt = ch.UpdatedAt,
                ParentId = ch.ParentId,
                RunCondition = ch.RunCondition,
                OnNodeDeath = ch.OnNodeDeath,
                Children = [],
            };

            _AttachNonTimedChildren(childEntity, ch.Id, depth + 1);

            children.Add(childEntity);
        }

        parentEntity.Children = children;
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

        return _cronJobs.TryGetValue(occurrence.CronJobId, out var definition)
            && !definition.IsPaused
            && (occurrence.Status == JobStatus.Idle || occurrence.Status == JobStatus.Queued)
            && (
                string.Equals(occurrence.OwnerId, _ownerId, StringComparison.Ordinal)
                || occurrence.LockedUntil == null
                || (occurrence.LockedUntil <= now && occurrence.OnNodeDeath == NodeDeathPolicy.Retry)
            );
    }

    private object _GetCronDefinitionLock(Guid cronJobId)
    {
        return _cronDefinitionLocks[_GetCronDefinitionLockIndex(cronJobId)];
    }

    private int _GetCronDefinitionLockIndex(Guid cronJobId)
    {
        return (int)((uint)cronJobId.GetHashCode() % (uint)_cronDefinitionLocks.Length);
    }

    private static TCronJob _CloneCronJob(TCronJob job)
    {
        return (TCronJob)job.Clone();
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
            CancelRequested = job.CancelRequested,
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
