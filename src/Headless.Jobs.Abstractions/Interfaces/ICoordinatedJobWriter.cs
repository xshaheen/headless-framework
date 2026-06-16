// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Jobs.Entities;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Narrow seam for writing job rows inside a caller-supplied relational transaction (commit coordination). It is
/// deliberately separate from <see cref="IJobPersistenceProvider{TTimeJob,TCronJob}" />: only the relational
/// (EF Core) provider implements it, so the in-memory provider needs no throwing stub and the public persistence
/// contract is unchanged. The manager discovers it by pattern-match
/// (<c>persistenceProvider is ICoordinatedJobWriter</c>); a relational coordinator that is active while the provider
/// is <em>not</em> an <see cref="ICoordinatedJobWriter{TTimeJob,TCronJob}" /> is a mis-wire and fails loud.
/// </summary>
/// <remarks>
/// Implementations write rows <b>only</b> — no immediate dispatch, scheduler restart, notification, or cache
/// invalidation. Those side effects are the manager's responsibility and are registered on
/// <c>ICommitCoordinator.OnCommit</c> so they fire only after the caller's transaction commits (and never on
/// rollback). <see cref="InvalidateCronExpressionsCacheAsync" /> is exposed here because the cron-expressions cache
/// is owned by the provider; the manager registers it on commit rather than letting it fire on a pre-commit snapshot.
/// </remarks>
[PublicAPI]
public interface ICoordinatedJobWriter<in TTimeJob, in TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    /// <summary>
    /// Writes the time-job rows inside the transaction surfaced by <paramref name="relationalContext" />, preserving
    /// insertion order. Does not dispatch, restart the scheduler, or notify — the manager defers those to commit.
    /// </summary>
    Task WriteTimeJobsAsync(
        TTimeJob[] jobs,
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes the cron-job rows inside the transaction surfaced by <paramref name="relationalContext" />, preserving
    /// insertion order. Does not invalidate the cron-expressions cache or notify — the manager defers those to commit.
    /// </summary>
    Task WriteCronJobsAsync(
        TCronJob[] jobs,
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Invalidates the cron-expressions cache. The manager registers this on <c>OnCommit</c> for the coordinated cron
    /// path so the cache is dropped only after the caller's transaction commits — never on a pre-commit snapshot.
    /// Best-effort: the durable store remains authoritative if the cache layer is unavailable.
    /// </summary>
    Task InvalidateCronExpressionsCacheAsync();
}
