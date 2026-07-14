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
    private readonly IJobsClaimStrategy<TTimeJob, TCronJob> _claimStrategy = claimStrategy;

    protected IDbContextFactory<TDbContext> DbContextFactory { get; } = dbContextFactory;

    protected ILogger Logger { get; } = logger;

    // Pickup-lease deadline window: every acquire stamps LockedUntil = now + LeaseDuration (KTD2).
    protected TimeSpan LeaseDuration { get; } = optionsBuilder.LeaseDuration;

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
    ) => _claimStrategy.ClaimTimeJobsAsync(timeJobs, cancellationToken);

    public IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobsAsync(CancellationToken cancellationToken) =>
        _claimStrategy.ClaimTimedOutTimeJobsAsync(cancellationToken);

    public async Task ReleaseAcquiredTimeJobsAsync(Guid[] timeJobIds, CancellationToken cancellationToken)
    {
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = TimeProvider.GetUtcNow().UtcDateTime;

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
                setter => setter.UpdateTimeJob(functionContexts, TimeProvider.GetUtcNow().UtcDateTime),
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
                setter => setter.UpdateTimeJob(functionContext, TimeProvider.GetUtcNow().UtcDateTime),
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
        var now = TimeProvider.GetUtcNow().UtcDateTime;

        // Define the window: ignore anything older than 1 second ago
        var oneSecondAgo = now.AddSeconds(-1);

        var baseQuery = dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.ExecutionTime >= oneSecondAgo) // Ignore old jobs (fallback handles them)
            .WhereCanAcquireUsingDatabaseClock(owner);

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

        return await baseQuery
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
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
        var now = TimeProvider.GetUtcNow().UtcDateTime;

        // Acquire and mark InProgress in a single update.
        // LEASE-DEADLINE WRITE — must stay AUTOCOMMIT. Do not wrap in an explicit transaction: on PostgreSQL the
        // EF-translated clock is `now()` (transaction-start), so a surrounding transaction would anchor the deadline
        // to transaction-open and shorten the lease by the transaction's duration. See the class-level note above.
        var affected = await dbContext
            .Set<TTimeJob>()
            .Where(x => ((IEnumerable<Guid>)ids).Contains(x.Id))
            .WhereCanAcquireUsingDatabaseClock(owner)
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

        // Return the acquired jobs for immediate execution, with children
        return await dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x =>
                ((IEnumerable<Guid>)ids).Contains(x.Id) && x.OwnerId == owner && x.Status == JobStatus.InProgress
            )
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
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
        var now = TimeProvider.GetUtcNow().UtcDateTime;

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
            // Delete related occurrences first (if any), then the cron jobs
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

        foreach (var (function, expression) in cronJobs)
        {
            if (existingByFunction.TryGetValue(function, out var cron))
            {
                // Update expression if it changed
                if (!string.Equals(cron.Expression, expression, StringComparison.Ordinal))
                {
                    cron.Expression = expression;
                    cron.UpdatedAt = now;
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
    ) => _claimStrategy.ClaimTimedOutCronJobOccurrencesAsync(cancellationToken);

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

        var now = TimeProvider.GetUtcNow().UtcDateTime;
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
    ) => _claimStrategy.ClaimCronJobOccurrencesAsync(cronJobOccurrences, cancellationToken);

    public async Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrenceAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        if (!OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return null!;
        }

        var now = TimeProvider.GetUtcNow().UtcDateTime;
        var mainSchedulerThreshold = now.AddSeconds(-1);
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurrence = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Include(x => x.CronJob)
            .Where(x => ((IEnumerable<Guid>)ids).Contains(x.CronJobId))
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

        var rowsToUpdate = dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => ((IEnumerable<Guid>)cronOccurrenceIds).Contains(x.Id))
            .WhereOwnedBy(owner);

        if (
            functionContext.PropertiesToUpdate.Contains(nameof(JobExecutionState.Status))
            && functionContext.Status == JobStatus.InProgress
        )
        {
            rowsToUpdate = rowsToUpdate.Where(x => x.Status == JobStatus.Queued);
        }

        var affected = await rowsToUpdate
            .ExecuteUpdateAsync(setter => setter.UpdateCronJobOccurrence(functionContext), cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return [];
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
