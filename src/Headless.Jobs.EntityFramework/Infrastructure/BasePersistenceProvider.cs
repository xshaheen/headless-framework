// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Infrastructure;

internal abstract class BasePersistenceProvider<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder,
    ICache? cache,
    IJobsClaimStrategy<TTimeJob, TCronJob> claimStrategy,
    ILogger logger
)
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    protected IDbContextFactory<TDbContext> DbContextFactory { get; } = dbContextFactory;

    protected ILogger Logger { get; } = logger;

    // Pickup-lease deadline window: every acquire stamps LockedUntil = now + LeaseDuration (KTD2).
    protected TimeSpan LeaseDuration { get; } = optionsBuilder.LeaseDuration;

    // R12/KTD2: the maximum number of nodes on a root-to-leaf path that hydration traverses (root = depth 1). A timed
    // descendant is a boundary — excluded from the in-tree walk, claimed independently (U5).
    protected int MaxChainDepth { get; } = optionsBuilder.MaxChainDepth;

    // Runtime owner accessor. Stamp/acquire sites read the current node@incarnation via TryGetStampOwner
    // and refuse to touch rows when membership is not established (registration pending or membership lost).
    protected IJobsOwnerIdentity OwnerIdentity { get; } = ownerIdentity;

    protected TimeProvider TimeProvider { get; } = timeProvider;

    // Feature-namespaced (jobs:) so the cron entry never collides with another feature's key when the host shares
    // one default ICache across features — matches the permissions:/features:/settings: convention.
    private const string _CronExpressionsCacheKey = "jobs:cron:expressions";

    private static readonly CacheEntryOptions _CronExpressionsCacheOptions = TimeSpan.FromMinutes(10);

    protected ICache? Cache { get; } = cache;

    // Ownership time (LockedUntil) is decided by the DATABASE clock, never the caller's TimeProvider: a lease is
    // written by one node and evaluated by another, so a shared authority is the only thing that makes the effective
    // lease duration independent of host skew. `DateTime.UtcNow` inside an ExecuteUpdate expression tree is NOT
    // evaluated in-process — EF translates it to the provider's server-time expression, so the comparison and the
    // stamp share one clock inside one statement, with no scalar clock round trip and no read-then-write gap.
    // Scheduling/observational time (ExecutedAt, candidate selection) stays on the injected TimeProvider so it
    // remains deterministic under FakeTimeProvider. See docs/solutions/design-patterns/temporal-authority-standard.md.
    //
    // WHY THE EF-TRANSLATED CLOCK IS SAFE HERE — AND THE INVARIANT THAT MAKES IT SO.
    // EF translates the clock to `now()` on Npgsql and `GETUTCDATE()` on SQL Server. On PostgreSQL `now()` is
    // TRANSACTION-START time, not statement time — the one function the temporal-authority standard otherwise tells
    // you never to use. It is correct here only because of a property this class maintains BY CONSTRUCTION:
    //
    //     A lease DEADLINE is never written inside an explicit transaction.
    //
    // The three `LockedUntil = <clock> + LeaseDuration` stamps (claim, renew-time-job, renew-cron-occurrence) each
    // run as a standalone autocommit statement, where `now()` == statement time == what we want. The multi-statement
    // transactions in this file only ever READ the lease (`LockedUntil <= now()`), RELEASE it (`LockedUntil = null`),
    // or stamp audit columns — none of which an instant frozen at transaction-open can corrupt: a lease that expires
    // mid-transaction is merely missed on this tick and reclaimed on the next (every such sweep is idempotent), and
    // audit stamps are off by the transaction's own duration (single-digit ms). SQL Server does not have this
    // question at all: `GETUTCDATE()` is evaluated per statement, and its only cost is `datetime` precision
    // (~3.33 ms), immaterial against minute-scale leases.
    //
    // KEEP IT THAT WAY. Wrapping a lease-deadline write in an explicit transaction would anchor that deadline to
    // transaction-open and silently SHORTEN the lease by the transaction's duration. A short lease is the dangerous
    // direction: it lets a second node reclaim the row while the owner still believes it holds it — the exact
    // double-dispatch this design exists to prevent. No analyzer enforces this.

    #region Core_Time_Ticker_Methods
    public IAsyncEnumerable<TimeJobEntity> QueueTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        CancellationToken cancellationToken
    )
    {
        return claimStrategy.ClaimTimeJobsAsync(timeJobs, cancellationToken);
    }

    public IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobsAsync(CancellationToken cancellationToken)
    {
        return claimStrategy.ClaimTimedOutTimeJobsAsync(cancellationToken);
    }

    public async Task ReleaseAcquiredTimeJobsAsync(Guid[] timeJobIds, CancellationToken cancellationToken)
    {
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = TimeProvider.GetUtcNow();

        var baseQuery =
            timeJobIds.Length == 0
                ? dbContext.Set<TTimeJob>()
                : dbContext.Set<TTimeJob>().Where(x => ((IEnumerable<Guid>)timeJobIds).Contains(x.Id));

        await baseQuery
            .WhereCanAcquireUsingDatabaseClock(owner)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.Status, _ => JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<int> UpdateTimeJobAsync(JobExecutionState functionContexts, CancellationToken cancellationToken)
    {
        // #5 completion fence: only the still-owning node may write a completion onto a non-terminal row.
        // A node the dead-node sweep already transitioned (MarkFailed/Skip -> terminal, or released -> owner
        // cleared) but which is actually alive must match 0 rows here instead of clobbering the sweep's result.
        // WhereOwnedBy = (Idle|Queued|InProgress) && OwnerId == owner, so terminal rows and reclaimed rows are excluded.
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return 0;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await dbContext
            .Set<TTimeJob>()
            .Where(x => x.Id == functionContexts.JobId)
            .WhereOwnedBy(owner)
            .ExecuteUpdateAsync(
                setter => setter.UpdateTimeJob(functionContexts, TimeProvider.GetUtcNow()),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<Guid[]> UpdateTimeJobsWithUnifiedContextAsync(
        Guid[] timeJobIds,
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        // #316/U5 claim→start ownership recheck: all unified writes are fenced by owner and non-terminal state.
        // Queued→InProgress additionally requires the row to still be Queued, so duplicate same-owner scheduler
        // wrappers cannot revalidate an already-running row. Run-condition skip writes retain the broader fence.
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return [];
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rowsToUpdate = dbContext
            .Set<TTimeJob>()
            .Where(x => ((IEnumerable<Guid>)timeJobIds).Contains(x.Id))
            .WhereOwnedBy(owner);

        if (
            functionContext.PropertiesToUpdate.Contains(nameof(JobExecutionState.Status))
            && functionContext.Status == JobStatus.InProgress
        )
        {
            rowsToUpdate = rowsToUpdate.Where(x => x.Status == JobStatus.Queued);
        }

        var affected = await rowsToUpdate
            .ExecuteUpdateAsync(
                setter => setter.UpdateTimeJob(functionContext, TimeProvider.GetUtcNow()),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return [];
        }

        var updated = dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => ((IEnumerable<Guid>)timeJobIds).Contains(x.Id))
            .Where(x => x.OwnerId == owner);

        if (functionContext.PropertiesToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            updated = updated.Where(x => x.Status == functionContext.Status);
        }

        return await updated.Select(x => x.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeJobEntity[]> GetEarliestTimeJobsAsync(CancellationToken cancellationToken)
    {
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return [];
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = TimeProvider.GetUtcNow();

        // Define the window: ignore anything older than 1 second ago
        var oneSecondAgo = now.UtcDateTime.AddSeconds(-1);

        var baseQuery = dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.ExecutionTime >= oneSecondAgo) // Ignore old jobs (fallback handles them)
            .WhereCanAcquireUsingDatabaseClock(owner)
            // U5/KTD3: a timed descendant surfaces here as its own candidate (excluded from the in-tree walk); the
            // parent gate keeps it out of the peek until its parent reached its matching terminal state.
            .WhereClaimableUnderParentTerminalGate(dbContext.Set<TTimeJob>());

        // Find the earliest job within our window
        var minExecutionTime = await baseQuery
            .OrderBy(x => x.ExecutionTime)
            .Select(x => x.ExecutionTime)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (minExecutionTime == null)
        {
            return [];
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

        // Fetch all jobs within that complete second (this ensures we get all jobs in the same second)
        var maxExecutionTime = minSecond.AddSeconds(1);

        // R12/KTD2: load the flat roots, then rebuild the non-timed in-tree subtree to MaxChainDepth in memory (a
        // recursive .Select projection is not EF-translatable) instead of a fixed-depth nested projection.
        return await _LoadWithDescendantsAsync(
                baseQuery
                    .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
                    .OrderBy(x => x.ExecutionTime),
                dbContext,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // R12/KTD2: projects a prepared time-job query to flat roots and rebuilds their non-timed in-tree subtree to
    // MaxChainDepth in memory (a recursive .Select projection is not EF-translatable). Shared by the peek and
    // immediate-acquire paths; the descendant reload always runs no-tracking against the same dbContext.
    private async Task<TimeJobEntity[]> _LoadWithDescendantsAsync(
        IQueryable<TTimeJob> source,
        TDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var roots = await source
            .Select(MappingExtensions.ForFlatTimeJob<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        await MappingExtensions
            .AttachNonTimedDescendantsAsync(
                dbContext.Set<TTimeJob>().AsNoTracking(),
                roots,
                MaxChainDepth,
                cancellationToken
            )
            .ConfigureAwait(false);

        return roots;
    }

    public async Task<byte[]> GetTimeJobRequestAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var request = await dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => x.Id == jobId)
            .Select(x => x.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return request ?? [];
    }

    public async Task<bool> RequestTimeJobCancellationAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = await dbContext
            .Set<TTimeJob>()
            .Where(x => x.Id == jobId && !x.CancelRequested)
            .Where(x => x.Status == JobStatus.Idle || x.Status == JobStatus.Queued || x.Status == JobStatus.InProgress)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.CancelRequested, true)
                        .SetProperty(x => x.Status, x => x.Status == JobStatus.Idle ? JobStatus.Cancelled : x.Status)
                        .SetProperty(
                            x => x.ExecutedAt,
                            x => x.Status == JobStatus.Idle ? DateTime.UtcNow : x.ExecutedAt
                        )
                        .SetProperty(x => x.OwnerId, x => x.Status == JobStatus.Idle ? null : x.OwnerId)
                        .SetProperty(x => x.LockedUntil, x => x.Status == JobStatus.Idle ? null : x.LockedUntil)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (affected != 1)
        {
            return false;
        }

        var resultingStatus = await dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => x.Id == jobId)
            .Select(x => x.Status)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (resultingStatus == JobStatus.Cancelled)
        {
            await _ApplyCancelledParentRunConditionsAsync(dbContext, jobId, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // U5/KTD3: _ApplyCancelledParentRunConditionsAsync (in the committed transaction) handled the NON-timed
        // children. The cancelled parent's TIMED children are reconciled by ApplyParentTerminalRunConditionsAsync,
        // driven post-cancellation by the manager so the released-child scheduler wake (RestartIfNeeded) is threaded
        // through the same path as the executor/sweep reconcile — and by the poll-time / sweep reconcile as a backstop.
        return true;
    }

    public async Task<bool?> IsTimeJobCancellationRequestedAsync(
        Guid jobId,
        CancellationToken cancellationToken = default
    )
    {
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return null;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => x.Id == jobId && x.OwnerId == owner && x.Status == JobStatus.InProgress)
            .Select(x => (bool?)x.CancelRequested)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task _ApplyCancelledParentRunConditionsAsync(
        TDbContext dbContext,
        Guid parentId,
        CancellationToken cancellationToken
    )
    {
        var jobs = dbContext.Set<TTimeJob>();
        await jobs.Where(x => x.ParentId == parentId && x.Status == JobStatus.Idle && x.ExecutionTime == null)
            .Where(x =>
                x.RunCondition == RunCondition.OnCancelled
                || x.RunCondition == RunCondition.OnFailureOrCancelled
                || x.RunCondition == RunCondition.OnAnyCompletedStatus
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.ExecutionTime, _ => DateTime.UtcNow)
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);

        var rejectedIds = await jobs.AsNoTracking()
            .Where(x => x.ParentId == parentId && x.Status == JobStatus.Idle && x.ExecutionTime == null)
            .Where(x =>
                x.RunCondition == null
                || (
                    x.RunCondition != RunCondition.OnCancelled
                    && x.RunCondition != RunCondition.OnFailureOrCancelled
                    && x.RunCondition != RunCondition.OnAnyCompletedStatus
                )
            )
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rejectedIds.Length == 0)
        {
            return;
        }

        await _SkipCancellationBranchAsync(
                jobs,
                rejectedIds,
                "Parent cancellation did not satisfy the job run condition.",
                cancellationToken
            )
            .ConfigureAwait(false);

        await _CascadeSkipSubtreeAsync(
                jobs,
                rejectedIds,
                "Ancestor job was skipped after parent cancellation.",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // Cascades the Idle-only skip from a set of already-skipped root ids down through their whole subtree, frontier by
    // frontier, tagging each descendant with <paramref name="reason"/>. The root ids themselves are skipped by the
    // caller before invoking this. Returns the number of rows skipped in the cascade.
    private static async Task<int> _CascadeSkipSubtreeAsync(
        DbSet<TTimeJob> jobs,
        Guid[] rootIds,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var skipped = 0;
        var frontier = rootIds;

        while (frontier.Length != 0)
        {
            var parentIds = frontier;
            frontier = await jobs.AsNoTracking()
                .Where(x => x.ParentId != null && ((IEnumerable<Guid>)parentIds).Contains(x.ParentId.Value))
                .Where(x => x.Status == JobStatus.Idle)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (frontier.Length != 0)
            {
                skipped += await _SkipCancellationBranchAsync(jobs, frontier, reason, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return skipped;
    }

    private static Task<int> _SkipCancellationBranchAsync(
        DbSet<TTimeJob> jobs,
        Guid[] jobIds,
        string reason,
        CancellationToken cancellationToken
    ) =>
        jobs.Where(x => ((IEnumerable<Guid>)jobIds).Contains(x.Id) && x.Status == JobStatus.Idle)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.SkippedReason, reason)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            );

    // U5/KTD3: cascade skip-reason for descendants of a timed child whose parent's run condition did not match; the
    // direct-child mismatch reason is the shared ChainRunConditionRules.RunConditionMismatchReason.
    private const string _AncestorSkippedReason =
        "Ancestor job was skipped after its parent's run condition did not match.";

    public async Task<DateTime?> ApplyParentTerminalRunConditionsAsync(
        Guid? parentId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var (earliest, _) = await _ReconcileParentTerminalTimedChildrenAsync(dbContext, parentId, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return earliest;
    }

    public async Task<int> SkipStrandedTimedChildrenAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobs = dbContext.Set<TTimeJob>();

        // Probe for a MISMATCHED stranded candidate BEFORE opening a transaction. This is the poll-time safety net
        // (R1: now gated to the fallback cadence), and it almost always finds nothing — so the common empty case must
        // not open (and hold) a transaction. Probing the MISMATCHED set (not merely "parent is terminal") is what
        // makes the sweep bounded and starvation-free: this path only ever skips, never releases, so a page full of
        // matching (release-side) children — which it never mutates — must not keep re-triggering the reconcile.
        var hasMismatchedCandidate = await _TimedChildReconcileCandidates(jobs)
            .AsNoTracking()
            .WhereParentTerminalRunConditionMismatched(jobs)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!hasMismatchedCandidate)
        {
            return 0;
        }

        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var skipped = await _SkipStrandedTimedChildrenBoundedAsync(dbContext, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return skipped;
    }

    // R2/KTD3/KTD6: the poll-time safety net's BOUNDED skip pass. Selects only the rows it mutates — IDLE gated timed
    // children whose parent reached a NON-matching terminal state — ordered and capped at the same batch size as the
    // sibling poll queries. Because every selected row leaves the candidate set once skipped, a large stranded backlog
    // drains monotonically across sweeps, and matching future children (which this path never touches) can never fill
    // the page and starve it. Only the SELECTION is bounded: the subtree cascade below is deliberately UNCAPPED — it
    // skips ANY idle descendant, while the candidate predicate requires ExecutionTime != null, so a half-finished
    // cascade would strand non-timed descendants under a Skipped ancestor with no path that ever re-selects them
    // (KTD6). The per-parent reconcile (ApplyParentTerminalRunConditionsAsync) stays exhaustive and unbounded; this
    // bound is the all-parents sweep's alone.
    private static async Task<int> _SkipStrandedTimedChildrenBoundedAsync(
        TDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var jobs = dbContext.Set<TTimeJob>();

        var mismatchedIds = await _TimedChildReconcileCandidates(jobs)
            .AsNoTracking()
            .WhereParentTerminalRunConditionMismatched(jobs)
            .OrderBy(x => x.ExecutionTime)
            .ThenBy(x => x.Id)
            .Take(JobsClaimStrategyDefaults.MaxClaimBatchSize)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (mismatchedIds.Length == 0)
        {
            return 0;
        }

        var skipped = await _SkipCancellationBranchAsync(
                jobs,
                mismatchedIds,
                ChainRunConditionRules.RunConditionMismatchReason,
                cancellationToken
            )
            .ConfigureAwait(false);

        skipped += await _CascadeSkipSubtreeAsync(jobs, mismatchedIds, _AncestorSkippedReason, cancellationToken)
            .ConfigureAwait(false);

        return skipped;
    }

    // Base predicate for the KTD3 timed-child reconcile: an IDLE, scheduled (ExecutionTime != null), parented timed
    // child whose run condition is parent-terminal-gated. Shared by the reconcile and its pre-transaction probe so the
    // two agree on candidate membership.
    private static IQueryable<TTimeJob> _TimedChildReconcileCandidates(DbSet<TTimeJob> jobs)
    {
        return jobs.Where(x =>
            x.Status == JobStatus.Idle
            && x.ExecutionTime != null
            && x.ParentId != null
            && (
                x.RunCondition == RunCondition.OnSuccess
                || x.RunCondition == RunCondition.OnFailure
                || x.RunCondition == RunCondition.OnCancelled
                || x.RunCondition == RunCondition.OnFailureOrCancelled
                || x.RunCondition == RunCondition.OnAnyCompletedStatus
            )
        );
    }

    // The set-based release/skip reconcile (KTD3). For every IDLE timed child (ExecutionTime != null) with a
    // parent-terminal-gated run condition whose parent has reached a terminal state: a MATCHING run condition releases
    // (a past-due child is re-stamped to the database clock now so the staleness-filtered main peek claims it
    // promptly); a NON-matching one is skipped with its whole subtree. parentId constrains to one parent (per-parent,
    // from the executor/cancellation); null reconciles all terminal parents. This is the RELEASE-and-skip path and is
    // intentionally UNBOUNDED — a terminalizing parent's children must all be reconciled, or the parent's subtree is
    // left half-settled. The all-parents SKIP-ONLY safety net is bounded separately (SkipStrandedTimedChildrenBounded,
    // R2/KTD6). Returns the earliest execution time among matching children (for RestartIfNeeded) and the number of
    // rows skipped. DB-clock discipline: DateTime.UtcNow is inside the ExecuteUpdate expression tree (translated to the
    // server clock), never a pre-evaluated local.
    private static async Task<(DateTime? Earliest, int Skipped)> _ReconcileParentTerminalTimedChildrenAsync(
        TDbContext dbContext,
        Guid? parentId,
        CancellationToken cancellationToken
    )
    {
        var jobs = dbContext.Set<TTimeJob>();

        var candidates = _TimedChildReconcileCandidates(jobs);

        if (parentId is { } target)
        {
            candidates = candidates.Where(x => x.ParentId == target);
        }

        // Children whose parent has reached ANY terminal state — the ones that need reconciling now.
        var terminalChildIds = await candidates
            .AsNoTracking()
            .WhereParentIsTerminal(jobs)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (terminalChildIds.Length == 0)
        {
            return (null, 0);
        }

        // Among those, the ones whose parent MATCHES their run condition — for a gated timed child the claim gate
        // reduces to exactly "parent matched".
        var matchingChildIds = await candidates
            .AsNoTracking()
            .Where(x => ((IEnumerable<Guid>)terminalChildIds).Contains(x.Id))
            .WhereClaimableUnderParentTerminalGate(jobs)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var matchingSet = matchingChildIds.ToHashSet();
        var skipIds = terminalChildIds.Where(id => !matchingSet.Contains(id)).ToArray();

        // SKIP the non-matching children and cascade the skip to their subtrees (mirror of the cancellation reconcile).
        var skipped = 0;
        if (skipIds.Length != 0)
        {
            skipped += await _SkipCancellationBranchAsync(
                    jobs,
                    skipIds,
                    ChainRunConditionRules.RunConditionMismatchReason,
                    cancellationToken
                )
                .ConfigureAwait(false);

            skipped += await _CascadeSkipSubtreeAsync(jobs, skipIds, _AncestorSkippedReason, cancellationToken)
                .ConfigureAwait(false);
        }

        if (matchingChildIds.Length == 0)
        {
            return (null, skipped);
        }

        // RELEASE the matching children whose execution time already passed: re-stamp to the database clock now so the
        // staleness-filtered main peek claims them promptly instead of the slow fallback. Future-dated matching
        // children keep their scheduled time (the gate already makes them claimable then).
        await candidates
            .Where(x => ((IEnumerable<Guid>)matchingChildIds).Contains(x.Id) && x.ExecutionTime <= DateTime.UtcNow)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.ExecutionTime, _ => DateTime.UtcNow)
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);

        // Earliest execution time among matching children (past-due ones now re-stamped to ~now) → RestartIfNeeded hint.
        var earliest = await jobs.AsNoTracking()
            .Where(x =>
                ((IEnumerable<Guid>)matchingChildIds).Contains(x.Id)
                && x.Status == JobStatus.Idle
                && x.ExecutionTime != null
            )
            .OrderBy(x => x.ExecutionTime)
            .Select(x => x.ExecutionTime)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (earliest, skipped);
    }

    public async Task<int> ReleaseDeadNodeTimeJobResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // #316 clock-skew: the InProgress lease-deferral arms compare LockedUntil <= now against the DB clock, not the
        // reclaiming node's TimeProvider, so a still-leased running row survives regardless of cross-node skew.

        // KTD6: a NodeLeft reclaim may race host shutdown; the writes must not be torn down mid-statement,
        // so they run under CancellationToken.None. The three statements are wrapped in one transaction
        // (finding 3.1) so a crash between them can't leave a half-reclaimed node — the idempotent reconcile
        // (U2) re-reclaims a partial node on the next tick, but the transaction removes the transient state.
        //
        // The explicit transaction freezes PostgreSQL's `now()` at transaction-open for all three statements. That is
        // SAFE here, and deliberately so: these statements only READ the lease (LockedUntil <= now), RELEASE it
        // (LockedUntil = null) and stamp audit columns — they never WRITE a lease deadline. A lease that expires
        // while the transaction is open is simply missed on this tick and reclaimed on the next (this sweep is
        // idempotent by design). Do not "fix" this by removing the transaction: that reintroduces the half-reclaimed
        // node the transaction exists to prevent. Equally, do not add a lease-deadline write here — see the
        // class-level note for why that would silently shorten the lease.
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        // Per-policy dead-node transition (#315, #316/U4). Idle/Queued never started → reclaimed immediately on node
        // death (fast recovery preserved). InProgress arms defer to the lease (LockedUntil <= now): a busy node's
        // still-leased running jobs survive a membership blip — once the (dead) node stops renewing, the lease lapses
        // and U3's stalled-reclaim recovers them within ≈ one lease TTL. Retry rows are released to Idle (InProgress
        // is invisible to the claim predicate, so they must be handed back, not left for the lease-expiry arm).
        var released = await dbContext
            .Set<TTimeJob>()
            .WhereOwnedBy(instanceIdentifier)
            .Where(x =>
                x.Status == JobStatus.Idle
                || x.Status == JobStatus.Queued
                || (
                    x.Status == JobStatus.InProgress
                    && x.OnNodeDeath == NodeDeathPolicy.Retry
                    && x.LockedUntil <= DateTime.UtcNow
                )
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        // MarkFailed: non-idempotent job that must not retry on node death — terminal Failed, once the lease lapsed.
        var failed = await dbContext
            .Set<TTimeJob>()
            .WhereOwnedBy(instanceIdentifier)
            .Where(x =>
                x.Status == JobStatus.InProgress
                && x.OnNodeDeath == NodeDeathPolicy.MarkFailed
                && x.LockedUntil <= DateTime.UtcNow
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Failed)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.ExceptionMessage, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        // Skip: idempotency-critical job that must never run twice — terminal Skipped, once the lease lapsed.
        var skipped = await dbContext
            .Set<TTimeJob>()
            .WhereOwnedBy(instanceIdentifier)
            .Where(x =>
                x.Status == JobStatus.InProgress
                && x.OnNodeDeath == NodeDeathPolicy.Skip
                && x.LockedUntil <= DateTime.UtcNow
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.SkippedReason, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        return released + failed + skipped;
    }
    #endregion

    public async Task<TimeJobEntity[]> AcquireImmediateTimeJobsAsync(
        Guid[]? ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids == null || ids.Length == 0)
        {
            return [];
        }

        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return [];
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = TimeProvider.GetUtcNow();

        // Acquire and mark InProgress in a single update.
        // LEASE-DEADLINE WRITE — must stay AUTOCOMMIT. Do not wrap in an explicit transaction: on PostgreSQL the
        // EF-translated clock is `now()` (transaction-start), so a surrounding transaction would anchor the deadline
        // to transaction-open and shorten the lease by the transaction's duration. See the class-level note above.
        var affected = await dbContext
            .Set<TTimeJob>()
            .Where(x => ((IEnumerable<Guid>)ids).Contains(x.Id))
            .WhereCanAcquireUsingDatabaseClock(owner)
            // U5/KTD3: gate the immediate-acquire path too — a timed descendant is claimable only once its parent
            // reached its matching terminal state. Roots (ParentId == null) pass trivially.
            .WhereClaimableUnderParentTerminalGate(dbContext.Set<TTimeJob>())
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, owner)
                        .SetProperty(x => x.LockedUntil, _ => DateTime.UtcNow.AddSeconds(LeaseDuration.TotalSeconds))
                        .SetProperty(x => x.Status, JobStatus.InProgress)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return [];
        }

        var jobs = dbContext.Set<TTimeJob>();

        // Which roots this call actually acquired — the batch UPDATE reports a count, not identities, and a racing
        // node may have taken some of them.
        var acquiredRootIds = await jobs.AsNoTracking()
            .Where(x =>
                ((IEnumerable<Guid>)ids).Contains(x.Id) && x.OwnerId == owner && x.Status == JobStatus.InProgress
            )
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        // Lease each acquired root's non-timed subtree BEFORE returning it for execution. The executor runs a chain by
        // in-process recursion and fences every node on lease renewal before invoking it, so a hydrated-but-unleased
        // descendant fails that fence and is stranded Idle forever — this is the immediate-dispatch counterpart of the
        // scheduled tree claim, and it must not be dropped.
        var claimedIdsByRoot = new Dictionary<Guid, HashSet<Guid>>(acquiredRootIds.Length);

        foreach (var acquiredRootId in acquiredRootIds)
        {
            claimedIdsByRoot[acquiredRootId] = await JobsSubtreeLeaseWalk
                .LeaseNonTimedDescendantsAsync(
                    jobs,
                    acquiredRootId,
                    owner,
                    MaxChainDepth,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        // Return the acquired jobs for immediate execution, with the non-timed in-tree subtree to MaxChainDepth
        // (R12/KTD2: flat root load + in-memory rebuild replaces a fixed-depth nested projection).
        var acquired = await _LoadWithDescendantsAsync(
                jobs.AsNoTracking().Where(x => ((IEnumerable<Guid>)acquiredRootIds).Contains(x.Id)),
                dbContext,
                cancellationToken
            )
            .ConfigureAwait(false);

        // KTD2: the hydrated tree may include nodes the lease walk stopped at; execute strictly the claimed set.
        foreach (var root in acquired)
        {
            if (claimedIdsByRoot.TryGetValue(root.Id, out var claimedIds))
            {
                TimeJobSubtreeOperations.PruneToClaimedSet(root, claimedIds);
            }
        }

        return acquired;
    }

    public async Task<int> RenewTimeJobLeaseAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // #316 sliding lease: slide LockedUntil forward while the job runs. Fenced on WhereOwnedBy (the #5
        // completion-fence shape: still owned + non-terminal), so a row the dead-node/stalled sweep already
        // reclaimed, terminalized, or whose owner changed matches 0 rows — the signal the caller turns into
        // cancel-on-loss (U2/KTD3). No separate liveness query: this UPDATE is the loss detector.
        // #461: a NEGATIVE return means coordination membership is not currently established (registration pending
        // or a transient blip) — distinct from 0 (genuinely not owned). The caller skips this renewal tick instead of
        // cancelling, so a momentary membership hiccup doesn't kill a healthy job; if it persists the lease lapses and
        // the stalled-reclaim sweep recovers the row per OnNodeDeath (same bound as a dead node).
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return -1;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        // #316 clock-skew: stamp the slid lease from the DB clock, not the local TimeProvider, so the deadline a
        // remote sweep later compares against shares one authority with the value written here.
        // LEASE-DEADLINE WRITE — must stay AUTOCOMMIT (see the class-level note). A surrounding explicit transaction
        // would anchor `now()` to transaction-open and silently shorten every renewal.

        return await dbContext
            .Set<TTimeJob>()
            .Where(x => x.Id == jobId)
            // Renewal slides a RUNNING lease only: an Idle/Queued row hasn't started, so extending its LockedUntil
            // would return 1 ("lease held") and suppress the cancel-on-loss signal. WhereOwnedBy alone permits
            // Idle|Queued|InProgress, so the explicit InProgress filter is required here.
            .Where(x => x.Status == JobStatus.InProgress)
            .WhereOwnedBy(owner)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockedUntil, _ => DateTime.UtcNow.AddSeconds(LeaseDuration.TotalSeconds))
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<int> ReclaimStalledTimeJobsAsync(CancellationToken cancellationToken = default)
    {
        // #316/U3 gap-closer: reclaim InProgress rows whose lease lapsed (LockedUntil <= now) on ANY node — not
        // owner-scoped, unlike the dead-node sweep, because the trigger is a stalled lease, not a declared node
        // death. A healthy renewing job keeps a future LockedUntil and never matches. Same per-policy transitions
        // and PR#456 terminal-row hygiene as ReleaseDeadNodeTimeJobResourcesAsync, wrapped in one transaction so a crash
        // between phases can't leave a half-reclaimed view (re-run is idempotent regardless).
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // #316 clock-skew: lease-expiry is decided by the DB clock, never the reclaiming node's TimeProvider.

        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var set = dbContext.Set<TTimeJob>();

        // The reclaim writes run under CancellationToken.None (mirroring the dead-node sweep, KTD6): a host-stop racing
        // the sweep must not tear down a per-policy transition mid-statement and revert the whole transaction.
        var released = await set.Where(x =>
                x.Status == JobStatus.InProgress
                && x.LockedUntil <= DateTime.UtcNow
                && x.OnNodeDeath == NodeDeathPolicy.Retry
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        var failed = await set.Where(x =>
                x.Status == JobStatus.InProgress
                && x.LockedUntil <= DateTime.UtcNow
                && x.OnNodeDeath == NodeDeathPolicy.MarkFailed
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Failed)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.ExceptionMessage, "Lease lapsed while running!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        var skipped = await set.Where(x =>
                x.Status == JobStatus.InProgress
                && x.LockedUntil <= DateTime.UtcNow
                && x.OnNodeDeath == NodeDeathPolicy.Skip
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.SkippedReason, "Lease lapsed while running!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        return released + failed + skipped;
    }

    #region Core_Cron_Ticker_Methods
    public async Task MigrateDefinedCronJobsAsync(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = TimeProvider.GetUtcNow();

        var functions = cronJobs.Select(x => x.Function).ToArray();
        var cronSet = dbContext.Set<TCronJob>();

        // Identify seeded cron jobs (created from in-memory definitions)
        const string seedPrefix = "MemoryTicker_Seeded_";

        var seededCron = await cronSet
            .Where(c => c.InitIdentifier != null && c.InitIdentifier.StartsWith(seedPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var newFunctionSet = functions.ToHashSet(StringComparer.Ordinal);

        // Delete seeded cron jobs whose function no longer exists in the code definitions
        var seededToDelete = seededCron.Where(c => !newFunctionSet.Contains(c.Function)).Select(c => c.Id).ToArray();

        if (seededToDelete.Length > 0)
        {
            foreach (var definitionId in seededToDelete.Order())
            {
                await cronSet
                    .Where(x => x.Id == definitionId)
                    .ExecuteUpdateAsync(
                        setter => setter.SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            // Parent rows are locked above in canonical order; delete children before parents for FK safety.
            await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .Where(o => ((IEnumerable<Guid>)seededToDelete).Contains(o.CronJobId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await cronSet
                .Where(c => ((IEnumerable<Guid>)seededToDelete).Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Load existing (remaining) cron jobs for the current function set
        var existing = await cronSet
            .Where(c => ((IEnumerable<string>)functions).Contains(c.Function))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByFunction = existing
            .GroupBy(c => c.Function, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var changedDefinitionIds = new List<Guid>();
        var orderedCronJobs = cronJobs
            .Select(x =>
                (
                    x.Function,
                    x.Expression,
                    Id: existingByFunction.TryGetValue(x.Function, out var existingDefinition)
                        ? existingDefinition.Id
                        : JobsSeedId.ForCronSeed(x.Function)
                )
            )
            .OrderBy(x => x.Id)
            .ToArray();

        foreach (var (function, expression, _) in orderedCronJobs)
        {
            if (existingByFunction.TryGetValue(function, out var cron))
            {
                // Update expression if it changed
                if (!string.Equals(cron.Expression, expression, StringComparison.Ordinal))
                {
                    await cronSet
                        .Where(x => x.Id == cron.Id)
                        .ExecuteUpdateAsync(
                            setter => setter.SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    await dbContext.Entry(cron).ReloadAsync(cancellationToken).ConfigureAwait(false);

                    if (!string.Equals(cron.Expression, expression, StringComparison.Ordinal))
                    {
                        cron.Expression = expression;
                        cron.ScheduleRevision++;
                        cron.UpdatedAt = now;
                        changedDefinitionIds.Add(cron.Id);
                    }
                }
            }
            else
            {
                // Insert new seeded cron job. The id is DETERMINISTIC (derived from the function) so two nodes seeding
                // the same new function concurrently target the same primary key — the DB dedups to a single row
                // instead of inserting two distinct-id rows and double-scheduling the function.
                var entity = new TCronJob
                {
                    Id = JobsSeedId.ForCronSeed(function),
                    Function = function,
                    Expression = expression,
                    InitIdentifier = $"MemoryTicker_Seeded_{function}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = [],
                };
                await cronSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (changedDefinitionIds.Count > 0)
            {
                await dbContext
                    .Set<CronJobOccurrenceEntity<TCronJob>>()
                    .Where(x =>
                        changedDefinitionIds.Contains(x.CronJobId)
                        && (x.Status == JobStatus.Idle || x.Status == JobStatus.Queued)
                    )
                    .ExecuteUpdateAsync(
                        setter =>
                            setter
                                .SetProperty(x => x.Status, JobStatus.Skipped)
                                .SetProperty(x => x.ExecutedAt, now)
                                .SetProperty(x => x.UpdatedAt, now)
                                .SetProperty(x => x.SkippedReason, "Cron definition updated")
                                .SetProperty(x => x.OwnerId, _ => null)
                                .SetProperty(x => x.LockedUntil, _ => null),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Expected case: a concurrent first-boot lost the deterministic-id primary-key race — the winner's rows
            // stand, so there is nothing to clean up; discard our now-redundant tracked inserts. Logged at Debug (the
            // common trigger is the benign race) so a genuine, non-race failure that leaves this node's schedule
            // unseeded until the next boot is still greppable rather than silently swallowed.
            Logger.LogCronSeedConflictDiscarded(ex);
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<CronJobEntity[]> GetAllCronJobExpressionsAsync(CancellationToken cancellationToken = default)
    {
        if (Cache is null)
        {
            return await _LoadCronJobExpressionsAsync(cancellationToken).ConfigureAwait(false);
        }

        CronJobEntity[]? loaded = null;
        var factoryFailed = false;

        try
        {
            var result = await Cache
                .GetOrAddAsync<CronJobEntity[]>(
                    _CronExpressionsCacheKey,
                    async ct =>
                    {
                        try
                        {
                            loaded = await _LoadCronJobExpressionsAsync(ct).ConfigureAwait(false);

                            return loaded;
                        }
                        catch
                        {
                            factoryFailed = true;

                            throw;
                        }
                    },
                    _CronExpressionsCacheOptions,
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Contract: the registered ICache must never persist a null or empty cron-expressions entry. A hit of
            // CacheValue.Null (HasValue=true, Value=null) or NoValue collapses to [] here and is read as a genuinely
            // empty cron table — the factory does not re-run on a hit, so a misbehaving provider that cached a
            // null/empty value would silently suppress all cron scheduling until the entry's TTL elapses. Providers
            // must cache only the real DB result; Jobs intentionally trusts HasValue/Value rather than re-querying.
            return result.HasValue ? result.Value ?? [] : [];
        }
#pragma warning disable ERP022, RCS1075
        catch (Exception exception) when (!factoryFailed && !_IsCallerCancellation(exception, cancellationToken))
        {
            // Cache read/write failures are non-authoritative for Jobs; the database remains the source of truth.
            // A cache-layer OperationCanceledException bound to a foreign/internal token (e.g. a Redis command
            // timeout) is an infrastructure failure and falls open to the DB; only genuine caller cancellation
            // propagates (see _IsCallerCancellation), matching FactoryCacheCoordinator's token-identity semantics.
            return loaded ?? await _LoadCronJobExpressionsAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning restore ERP022, RCS1075
    }

    private static bool _IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
    {
        // Mirrors FactoryCacheCoordinator.IsCallerCancellation (Headless.Caching.Core, not a dependency here):
        // a cancellation is the caller's only when the caller token requested it or the OCE is bound to that exact
        // token. An OCE carrying a different/None token is a downstream timeout, not caller cancellation.
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        return cancellationToken.CanBeCanceled
            && exception is OperationCanceledException operationCanceled
            && operationCanceled.CancellationToken == cancellationToken;
    }

    protected async Task InvalidateCronExpressionsCacheAsync()
    {
        if (Cache is null)
        {
            return;
        }

        try
        {
            // Best-effort housekeeping AFTER the cron write has committed: decoupled from the caller token so a
            // cancellation racing the commit-to-invalidate window cannot leave the cache stale for the full TTL.
            // Mirrors FactoryCacheCoordinator's restamp, which uses CancellationToken.None for the same reason.
            await Cache.RemoveAsync(_CronExpressionsCacheKey, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable ERP022, RCS1075
        catch (Exception exception)
        {
            // Cache invalidation is best-effort; cron writes have already committed to the durable store. Log at
            // Warning so a recurring cache outage on the durable scheduler path (which would otherwise serve stale
            // cron expressions cluster-wide until the TTL elapses) is observable rather than silent.
            Logger.LogCronExpressionsCacheInvalidationFailed(exception, _CronExpressionsCacheKey);
        }
#pragma warning restore ERP022, RCS1075
    }

    private async Task<CronJobEntity[]> _LoadCronJobExpressionsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .Select(MappingExtensions.ForCronJobExpressions<CronJobEntity>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CronDispatchCandidates?> GetEarliestCronDispatchCandidatesAsync(
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // One indexed range scan over (IsPaused, NextDueUtc). The store instant rides along in the same statement, so
        // the caller's due-ness comparison is made against the server's clock rather than this node's, with no extra
        // round trip. Uncached by construction — the schedule position moves on every advance.
        var rows = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .Where(x => !x.IsPaused)
            .OrderBy(x => x.NextDueUtc)
            .ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.Function,
                x.Expression,
                x.TimeZoneId,
                x.ScheduleRevision,
                x.ReconciledThroughUtc,
                x.NextDueUtc,
                x.Retries,
                x.RetryIntervals,
                x.OnNodeDeath,
                StoreUtcNow = DateTime.UtcNow,
            })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Length == 0)
        {
            return null;
        }

        return new CronDispatchCandidates
        {
            StoreUtcNow = rows[0].StoreUtcNow,
            Candidates =
            [
                .. rows.Select(x => new CronDispatchCandidate
                {
                    CronJobId = x.Id,
                    Function = x.Function,
                    Expression = x.Expression,
                    TimeZoneId = x.TimeZoneId,
                    ScheduleRevision = x.ScheduleRevision,
                    ReconciledThroughUtc = x.ReconciledThroughUtc,
                    NextDueUtc = x.NextDueUtc,
                    Retries = x.Retries,
                    RetryIntervals = x.RetryIntervals,
                    OnNodeDeath = x.OnNodeDeath,
                }),
            ],
        };
    }

    public async Task<CronScheduleAdvanceResult?> AdvanceCronScheduleAsync(
        CronScheduleAdvance advance,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var definitions = dbContext.Set<TCronJob>();

        var fenced = definitions.WhereScheduleAdvanceFenceHolds(
            advance.CronJobId,
            advance.ObservedReconciledThroughUtc,
            advance.ExpectedScheduleRevision
        );

        if (advance.RequireProjectionDue)
        {
            fenced = fenced.WhereProjectionIsDueUsingDatabaseClock();
        }

        // AUTOCOMMIT BY CONSTRUCTION — do not wrap this in an explicit transaction. The due-ness arm of the fence and
        // the store instant returned below are both server-clock reads, and PostgreSQL's now() is TRANSACTION-START
        // time: inside a transaction the comparison and the returned instant would both be stale by the transaction's
        // age, so a definition would look due (or not) as of when the transaction opened rather than now. See the
        // class-level lease-clock note above and docs/solutions/design-patterns/temporal-authority-standard.md.
        //
        // A single UPDATE needs no transaction to be atomic, and the watermark equality in the fence is a value CAS —
        // so a losing racer matches zero rows, returns null, and leaves nothing to roll back. Atomicity with the
        // occurrence work that accompanies an advance is provided by self-healing rather than by this statement: a
        // crash in between leaves a watermark with no occurrence, and the next wake re-derives the projection from the
        // persisted watermark and materializes it, idempotently against the filtered uniqueness index.
        //
        // The local copies exist so the ExecuteUpdate expression trees capture plain DateTime values; capturing
        // `advance` would put a property access on the record inside the tree for EF to translate.
        var reconciledThroughUtc = advance.ReconciledThroughUtc;
        var nextDueUtc = advance.NextDueUtc;

        var affected = await fenced
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.ReconciledThroughUtc, reconciledThroughUtc)
                        .SetProperty(x => x.NextDueUtc, nextDueUtc),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return null;
        }

        // Read the committed values back instead of echoing the request (KTD4), so the caller sees exactly what the
        // store decided: both providers truncate to their column precision. DateTime.UtcNow in this projection is
        // translated to server time, which is how the store's clock reaches the caller without a scalar clock query
        // and without hijacking UpdatedAt — that column is observational time and stays on the injected TimeProvider.
        var committed = await definitions
            .AsNoTracking()
            .Where(x => x.Id == advance.CronJobId)
            .Select(x => new
            {
                x.ReconciledThroughUtc,
                x.NextDueUtc,
                StoreUtcNow = DateTime.UtcNow,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The definition can be deleted between the advance and this read-back. That is the same "this advance no
        // longer applies" outcome a lost fence produces, so report it the same way rather than throwing out of the
        // scheduler's poll.
        if (committed is null)
        {
            return null;
        }

        // Deliberately NOT invalidating the cron-expressions cache: its projection
        // (MappingExtensions.ForCronJobExpressions) carries no schedule-position field, so it cannot serve a stale
        // watermark, and dropping the entry on every advance would evict it on every scheduler tick — the opposite of
        // why it exists. Any future field added to that projection must revisit this.
        return new CronScheduleAdvanceResult
        {
            ReconciledThroughUtc = committed.ReconciledThroughUtc,
            NextDueUtc = committed.NextDueUtc,
            StoreUtcNow = committed.StoreUtcNow,
        };
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public async Task<int> UpdateCronJobOccurrenceAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken
    )
    {
        // #5 completion fence (see UpdateTimeJobAsync): only the still-owning node may complete a non-terminal occurrence.
        // Returns 0 when fenced out (foreign owner / terminal row), 1 when applied — mirroring UpdateTimeJobAsync.
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return 0;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => x.Id == functionContext.JobId)
            .WhereOwnedBy(owner)
            .ExecuteUpdateAsync(setter => setter.UpdateCronJobOccurrence(functionContext), cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueTimedOutCronJobOccurrencesAsync(
        CancellationToken cancellationToken = default
    )
    {
        return claimStrategy.ClaimTimedOutCronJobOccurrencesAsync(cancellationToken);
    }

    public async Task<int> ReleaseDeadNodeOccurrenceResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // #316 clock-skew: InProgress lease-deferral arms compare LockedUntil <= now against the DB clock (see
        // ReleaseDeadNodeTimeJobResourcesAsync).

        // See ReleaseDeadNodeTimeJobResourcesAsync: strict WhereOwnedBy (KTD5/R4), one transaction (finding 3.1),
        // CancellationToken.None for the reclaim writes (KTD6).
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        // Per-policy dead-node transition (#315, #316/U4) — mirrors ReleaseDeadNodeTimeJobResourcesAsync. Idle/Queued
        // reclaimed immediately; InProgress arms defer to the lease (LockedUntil <= now) so a still-leased running
        // occurrence survives a membership blip and is recovered by U3 once its lease lapses.
        var released = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .WhereOwnedBy(instanceIdentifier)
            .Where(x =>
                x.Status == JobStatus.Idle
                || x.Status == JobStatus.Queued
                || (
                    x.Status == JobStatus.InProgress
                    && x.OnNodeDeath == NodeDeathPolicy.Retry
                    && x.LockedUntil <= DateTime.UtcNow
                )
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        var failed = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .WhereOwnedBy(instanceIdentifier)
            .Where(x =>
                x.Status == JobStatus.InProgress
                && x.OnNodeDeath == NodeDeathPolicy.MarkFailed
                && x.LockedUntil <= DateTime.UtcNow
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Failed)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.ExceptionMessage, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        var skipped = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .WhereOwnedBy(instanceIdentifier)
            .Where(x =>
                x.Status == JobStatus.InProgress
                && x.OnNodeDeath == NodeDeathPolicy.Skip
                && x.LockedUntil <= DateTime.UtcNow
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.SkippedReason, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        return released + failed + skipped;
    }

    public async Task<int> RenewCronJobOccurrenceLeaseAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken = default
    )
    {
        // #316 sliding lease — mirror of RenewTimeJobLeaseAsync for cron occurrences. WhereOwnedBy fence makes a
        // lost/reclaimed/terminalized occurrence match 0 rows -> cancel-on-loss (U2/KTD3).
        // #461: a NEGATIVE return means coordination membership is not established (see RenewTimeJobLeaseAsync) — the
        // caller skips the renewal tick rather than cancelling.
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return -1;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        // #316 clock-skew: stamp the slid lease from the DB clock (see RenewTimeJobLeaseAsync).
        // LEASE-DEADLINE WRITE — must stay AUTOCOMMIT (see the class-level note). A surrounding explicit transaction
        // would anchor `now()` to transaction-open and silently shorten every renewal.

        return await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => x.Id == occurrenceId)
            // Renewal slides a RUNNING lease only (see RenewTimeJobLeaseAsync) — InProgress filter required.
            .Where(x => x.Status == JobStatus.InProgress)
            .WhereOwnedBy(owner)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockedUntil, _ => DateTime.UtcNow.AddSeconds(LeaseDuration.TotalSeconds))
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<int> ReclaimStalledCronJobOccurrencesAsync(CancellationToken cancellationToken = default)
    {
        // #316/U3 — cron mirror of ReclaimStalledTimeJobsAsync. Reclaim lapsed-lease InProgress occurrences on any node.
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // #316 clock-skew: lease-expiry is decided by the DB clock, never the reclaiming node's TimeProvider.

        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var set = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();

        // Reclaim writes under CancellationToken.None (see ReclaimStalledTimeJobsAsync / KTD6).
        var released = await set.Where(x =>
                x.Status == JobStatus.InProgress
                && x.LockedUntil <= DateTime.UtcNow
                && x.OnNodeDeath == NodeDeathPolicy.Retry
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        var failed = await set.Where(x =>
                x.Status == JobStatus.InProgress
                && x.LockedUntil <= DateTime.UtcNow
                && x.OnNodeDeath == NodeDeathPolicy.MarkFailed
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Failed)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.ExceptionMessage, "Lease lapsed while running!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        var skipped = await set.Where(x =>
                x.Status == JobStatus.InProgress
                && x.LockedUntil <= DateTime.UtcNow
                && x.OnNodeDeath == NodeDeathPolicy.Skip
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.SkippedReason, "Lease lapsed while running!")
                        .SetProperty(x => x.ExecutedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        return released + failed + skipped;
    }

    public async Task ReleaseAcquiredCronJobOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return;
        }

        var now = TimeProvider.GetUtcNow();
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseQuery =
            occurrenceIds.Length == 0
                ? dbContext.Set<CronJobOccurrenceEntity<TCronJob>>()
                : dbContext
                    .Set<CronJobOccurrenceEntity<TCronJob>>()
                    .Where(x => ((IEnumerable<Guid>)occurrenceIds).Contains(x.Id));

        await baseQuery
            .WhereCanAcquireUsingDatabaseClock(owner)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // KTD7: cron-occurrence creation is intentionally NOT guarded by a coarse 'jobs.cron-occurrence-creation'
    // distributed lock. First creation is deduplicated by (ExecutionTime, CronJobId); requeues of known occurrences
    // update by id. Storage-level dedup is the correctness boundary here. A coarse lock would only serialize
    // independent occurrences for no benefit. Revisit only if evidence shows storage dedup is insufficient (see plan
    // #267 deferred follow-up).
    public IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        CancellationToken cancellationToken = default
    )
    {
        return claimStrategy.ClaimCronJobOccurrencesAsync(cronJobOccurrences, cancellationToken);
    }

    public async Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrenceAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return null!;
        }

        var now = TimeProvider.GetUtcNow();
        var mainSchedulerThreshold = now.UtcDateTime.AddSeconds(-1);
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurrence = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Include(x => x.CronJob)
            // An empty id set searches every definition, per the interface contract and the in-memory provider. The
            // scheduler relies on that: it no longer enumerates all definitions to build this filter, so passing the
            // full id set would reintroduce exactly the load-everything read the projection path removed. The
            // remaining predicates (window, pause, acquirability) plus OrderBy/FirstOrDefault already bound the scan.
            .Where(x => ids.Length == 0 || ((IEnumerable<Guid>)ids).Contains(x.CronJobId))
            .Where(x => !x.CronJob.IsPaused)
            .Where(x => x.ExecutionTime >= mainSchedulerThreshold) // Only items within the 1-second main scheduler window
            .WhereCanAcquireUsingDatabaseClock(owner)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForLatestQueuedCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>())
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return occurrence!;
    }

    public async Task<byte[]> GetCronJobOccurrenceRequestAsync(
        Guid jobId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var request = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Include(x => x.CronJob)
            .Where(x => x.Id == jobId)
            .Select(x => x.CronJob.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return request ?? [];
    }

    public async Task<Guid[]> UpdateCronJobOccurrencesWithUnifiedContextAsync(
        Guid[] cronOccurrenceIds,
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        // #316/U5 — cron mirror of UpdateTimeJobsWithUnifiedContextAsync, including the strict Queued→InProgress
        // transition that rejects duplicate same-owner scheduler wrappers.
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return [];
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var startsExecution =
            functionContext.PropertiesToUpdate.Contains(nameof(JobExecutionState.Status))
            && functionContext.Status == JobStatus.InProgress;
        await using var transaction = startsExecution
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        if (startsExecution)
        {
            var definitionIds = await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .AsNoTracking()
                .Where(x => ((IEnumerable<Guid>)cronOccurrenceIds).Contains(x.Id))
                .Select(x => x.CronJobId)
                .Distinct()
                .Order()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var definitionId in definitionIds)
            {
                var active = await dbContext
                    .Set<TCronJob>()
                    .Where(x => x.Id == definitionId && !x.IsPaused)
                    .ExecuteUpdateAsync(
                        setter => setter.SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (active == 0)
                {
                    return [];
                }
            }
        }

        var rowsToUpdate = dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => ((IEnumerable<Guid>)cronOccurrenceIds).Contains(x.Id))
            .WhereOwnedBy(owner);

        if (startsExecution)
        {
            rowsToUpdate = rowsToUpdate.Where(x => x.Status == JobStatus.Queued && !x.CronJob.IsPaused);
        }

        var affected = await rowsToUpdate
            .ExecuteUpdateAsync(setter => setter.UpdateCronJobOccurrence(functionContext), cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return [];
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var updated = dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Where(x => ((IEnumerable<Guid>)cronOccurrenceIds).Contains(x.Id))
            .Where(x => x.OwnerId == owner);

        if (functionContext.PropertiesToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            updated = updated.Where(x => x.Status == functionContext.Status);
        }

        return await updated.Select(x => x.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion
}

internal static partial class BasePersistenceProviderLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "Cron-expressions cache invalidation failed for key '{Key}'; stale cron expressions may be served "
            + "until the cache entry's TTL elapses. Cache is fail-open and the database remains authoritative."
    )]
    public static partial void LogCronExpressionsCacheInvalidationFailed(
        this ILogger logger,
        Exception exception,
        string key
    );

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "Cron-seed migration hit a DbUpdateException and discarded its redundant inserts. The expected cause "
            + "is a concurrent first-boot losing the deterministic-id primary-key race (benign — the winner's rows "
            + "stand); any other cause leaves this node's schedule unseeded until the next boot reconciles it."
    )]
    public static partial void LogCronSeedConflictDiscarded(this ILogger logger, Exception exception);
}
