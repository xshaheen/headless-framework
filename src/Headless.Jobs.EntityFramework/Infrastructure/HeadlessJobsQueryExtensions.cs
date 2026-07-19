// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class HeadlessJobsQueryExtensions
{
    /// <summary>
    /// Selects acquirable non-terminal rows using the caller-supplied clock. EF runtime claim paths use the internal
    /// database-clock variant; this overload remains available for deterministic query composition.
    /// </summary>
    public static IQueryable<TTimeJob> WhereCanAcquire<TTimeJob>(
        this IQueryable<TTimeJob> q,
        string ownerId,
        DateTime now
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
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

    /// <summary>Relational runtime variant whose clock expression is translated inside the claim statement.</summary>
    internal static IQueryable<TTimeJob> WhereCanAcquireUsingDatabaseClock<TTimeJob>(
        this IQueryable<TTimeJob> q,
        string ownerId
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        // A non-terminal row is claimable if it is already mine (crash re-pickup), never leased, or its lease
        // deadline has passed (lease-expiry self-heal). DateTime.UtcNow is provider-translated to the database clock,
        // so comparison and stamping share one authority without a separate scalar query.
        // The lease-expiry arm is gated on OnNodeDeath == Retry (KTD5/#315): only idempotent jobs are speculatively
        // re-claimed when their lease lapses; MarkFailed/Skip rows are left for the dead-node sweep to transition.
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued)
            && (
                e.OwnerId == ownerId
                || e.LockedUntil == null
                || (e.LockedUntil <= DateTime.UtcNow && e.OnNodeDeath == NodeDeathPolicy.Retry)
            )
        );
    }

    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanAcquireUsingDatabaseClock<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string ownerId
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued)
            && (
                e.OwnerId == ownerId
                || e.LockedUntil == null
                || (e.LockedUntil <= DateTime.UtcNow && e.OnNodeDeath == NodeDeathPolicy.Retry)
            )
        );
    }

    /// <summary>
    /// Selects the rows the fallback sweep may claim: <c>Idle</c>, or <c>Queued</c> with a lapsed or absent lease
    /// (<c>LockedUntil == null || LockedUntil &lt;= now</c>). Unlike <c>WhereCanAcquire</c> this is the owner-agnostic
    /// fallback predicate — <paramref name="now"/> is supplied by the caller.
    /// </summary>
    public static IQueryable<TTimeJob> WhereCanFallbackClaim<TTimeJob>(this IQueryable<TTimeJob> q, DateTime now)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            e.Status == JobStatus.Idle
            || (e.Status == JobStatus.Queued && (e.LockedUntil == null || e.LockedUntil <= now))
        );
    }

    /// <inheritdoc cref="WhereCanFallbackClaim{TTimeJob}(System.Linq.IQueryable{TTimeJob},System.DateTime)"/>
    public static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanFallbackClaim<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        DateTime now
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            e.Status == JobStatus.Idle
            || (e.Status == JobStatus.Queued && (e.LockedUntil == null || e.LockedUntil <= now))
        );
    }

    internal static IQueryable<TTimeJob> WhereCanFallbackClaimUsingDatabaseClock<TTimeJob>(this IQueryable<TTimeJob> q)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            e.Status == JobStatus.Idle
            || (e.Status == JobStatus.Queued && (e.LockedUntil == null || e.LockedUntil <= DateTime.UtcNow))
        );
    }

    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanFallbackClaimUsingDatabaseClock<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            e.Status == JobStatus.Idle
            || (e.Status == JobStatus.Queued && (e.LockedUntil == null || e.LockedUntil <= DateTime.UtcNow))
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
