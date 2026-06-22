// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Infrastructure;

public static class JobsQueryExtensions
{
    public static IQueryable<TTimeJob> WhereCanAcquire<TTimeJob>(
        this IQueryable<TTimeJob> q,
        string ownerId,
        DateTime now
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        // A non-terminal row is claimable if it is already mine (crash re-pickup), never leased, or its lease
        // deadline has passed (lease-expiry self-heal). `now` is the injected application clock (KTD1), bound as a
        // parameter so EF translates `LockedUntil <= @now` — never the DB server clock, for InMemory↔SQL parity.
        // The lease-expiry arm is gated on OnNodeDeath == Retry (KTD5/#315): only idempotent jobs are speculatively
        // re-claimed when their lease lapses; MarkFailed/Skip rows are left for the dead-node sweep to transition.
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued)
            && (
                e.OwnerId == ownerId
                || e.LockedUntil == null
                || (e.LockedUntil <= now && e.OnNodeDeath == NodeDeathPolicy.Retry)
            )
        );
    }

    public static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanAcquire<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string ownerId,
        DateTime now
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued)
            && (
                e.OwnerId == ownerId
                || e.LockedUntil == null
                || (e.LockedUntil <= now && e.OnNodeDeath == NodeDeathPolicy.Retry)
            )
        );
    }

    /// <summary>
    /// Selects the non-terminal rows owned by <paramref name="owner"/> for dead-node reclaim. Unlike
    /// <c>WhereCanAcquire</c> this drops the loose unowned/lease-expired arms (KTD5/R4): a survivor reacting
    /// to a dead incarnation reclaims only that incarnation's rows — never unowned-but-idle rows nor a
    /// fast-restart's freshly-stamped rows. The terminal-state guard is preserved (terminal rows excluded).
    /// </summary>
    public static IQueryable<TTimeJob> WhereOwnedBy<TTimeJob>(this IQueryable<TTimeJob> q, string owner)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued || e.Status == JobStatus.InProgress)
            && e.OwnerId == owner
        );
    }

    /// <inheritdoc cref="WhereOwnedBy{TTimeJob}(System.Linq.IQueryable{TTimeJob},string)"/>
    public static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereOwnedBy<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string owner
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued || e.Status == JobStatus.InProgress)
            && e.OwnerId == owner
        );
    }
}
