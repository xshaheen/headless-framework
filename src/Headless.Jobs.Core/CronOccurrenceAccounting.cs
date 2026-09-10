// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs;

/// <summary>
/// One cron occurrence row reduced to the three booleans every occupied-instant decision needs, with the raw
/// <c>Status</c> deliberately absent.
/// </summary>
/// <remarks>
/// Status is persisted as a string-backed enum, so a value written by a newer binary would throw on materialization
/// if it were projected as <see cref="JobStatus" />. Every flag here is computed by the database (or by the compiled
/// projector in-memory) from string comparisons that cannot throw, and an unrecognized status therefore lands as
/// "not live, not repurposable, accounts for its instant" — the fail-closed answer.
/// </remarks>
[PublicAPI]
public sealed class CronOccurrenceInstantView
{
    /// <summary>Identity of the occurrence row.</summary>
    public Guid Id { get; init; }

    /// <summary>The definition this row belongs to, so one read can answer for a whole claim wave.</summary>
    public Guid CronJobId { get; init; }

    /// <summary>The instant this row stands at.</summary>
    public DateTime ExecutionTime { get; init; }

    /// <summary>Creation timestamp, used as the stable tiebreak when several rows share an instant.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The recovery stamp carried by the row, or <see langword="null" /> for an ordinary dispatch.</summary>
    public DateTime? RecoveredFromUtc { get; init; }

    /// <summary>Whether the row is still in the live lifecycle (<c>Idle</c>, <c>Queued</c>, or <c>InProgress</c>).</summary>
    public bool IsLive { get; init; }

    /// <summary>Whether recovery may repurpose the row in place (<c>Idle</c> or <c>Queued</c> — never <c>InProgress</c>).</summary>
    public bool IsRepurposable { get; init; }

    /// <summary>Whether this row stands for its instant, so no further occurrence may be materialized there.</summary>
    public bool AccountsForInstant { get; init; }
}

/// <summary>
/// The single occupied-instant accounting rule (KTD1), shared by every provider and by both the materialization and
/// the recovery path so the two can never disagree about the same row.
/// </summary>
/// <remarks>
/// <para>The matrix is total over every <see cref="JobStatus" /> and fails closed:</para>
/// <list type="bullet">
/// <item><description><c>Idle</c> / <c>Queued</c> / <c>InProgress</c> — suppress; a live row owns the instant.</description></item>
/// <item><description><c>Succeeded</c> / <c>DueDone</c> / <c>Failed</c> / <c>Cancelled</c> — suppress; the instant ran, and
/// re-running it is the retry path's business, not materialization's.</description></item>
/// <item><description><c>Skipped</c> with <see cref="CronOccurrenceDisposition.ReplacementOwed" /> — ALLOW; the seeding
/// migration retired the row without a replacement, so the fire is still owed.</description></item>
/// <item><description><c>Skipped</c> with any other disposition — suppress. That covers
/// <see cref="CronOccurrenceDisposition.Superseded" /> (a runtime edit already issued the replacement, so a re-fire
/// would double-run every expression edit), pause, lapsed leases, user-code skips, and <c>"Node is not alive!"</c>.
/// The dead-owner case never executed, but getting it re-run belongs to the reclaim and recovery path;
/// re-materializing at claim time would race that path and risk a duplicate.</description></item>
/// <item><description>Any unrecognized persisted status — suppress. The rule is expressed as "not (<c>Skipped</c> and
/// <c>ReplacementOwed</c>)", so an unknown value can only fall on the suppressing side.</description></item>
/// </list>
/// <para>
/// The relational providers that build raw SQL express the same rule through <c>CronOccurrenceAccountingSql</c>,
/// which is derived from <see cref="UnaccountedStatus" /> and <see cref="UnaccountedDisposition" /> rather than
/// restating it.
/// </para>
/// </remarks>
[PublicAPI]
public static class CronOccurrenceAccounting
{
    /// <summary>The only status that can fail to account for its instant.</summary>
    public const JobStatus UnaccountedStatus = JobStatus.Skipped;

    /// <summary>The only disposition that, paired with <see cref="UnaccountedStatus" />, owes another fire.</summary>
    public const CronOccurrenceDisposition UnaccountedDisposition = CronOccurrenceDisposition.ReplacementOwed;

    /// <summary>
    /// Projects occurrence rows onto <see cref="CronOccurrenceInstantView" />. Pass it to <c>Select</c> so the
    /// accounting flags are evaluated by the database and the raw status is never materialized.
    /// </summary>
    /// <typeparam name="TCronJob">The concrete cron definition type the occurrence belongs to.</typeparam>
    public static Expression<
        Func<CronJobOccurrenceEntity<TCronJob>, CronOccurrenceInstantView>
    > InstantViewSelector<TCronJob>()
        where TCronJob : CronJobEntity
    {
        return static x => new CronOccurrenceInstantView
        {
            Id = x.Id,
            CronJobId = x.CronJobId,
            ExecutionTime = x.ExecutionTime,
            CreatedAt = x.CreatedAt,
            RecoveredFromUtc = x.RecoveredFromUtc,
            IsLive = x.Status == JobStatus.Idle || x.Status == JobStatus.Queued || x.Status == JobStatus.InProgress,
            IsRepurposable = x.Status == JobStatus.Idle || x.Status == JobStatus.Queued,
            AccountsForInstant = x.Status != UnaccountedStatus || x.Disposition != UnaccountedDisposition,
        };
    }

    /// <summary>
    /// The compiled form of <see cref="InstantViewSelector{TCronJob}" />, for providers holding materialized entities
    /// rather than a queryable. Compiling the same expression is what keeps the rule single-sourced.
    /// </summary>
    /// <typeparam name="TCronJob">The concrete cron definition type the occurrence belongs to.</typeparam>
    public static Func<CronJobOccurrenceEntity<TCronJob>, CronOccurrenceInstantView> InstantViewProjector<TCronJob>()
        where TCronJob : CronJobEntity
    {
        return CompiledInstantViewProjector<TCronJob>.Value;
    }

    /// <summary>
    /// Whether any row standing at one instant accounts for it. Deliberately an aggregate over every row rather than
    /// a test of the first: several rows can share an instant (the filtered unique index constrains only the live
    /// ones), and if ANY of them accounts, the instant is taken.
    /// </summary>
    /// <param name="rowsAtInstant">Every row projected at a single <c>(CronJobId, ExecutionTime)</c> pair.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rowsAtInstant" /> is <see langword="null" />.</exception>
    public static bool IsInstantAccountedFor(IEnumerable<CronOccurrenceInstantView> rowsAtInstant)
    {
        Argument.IsNotNull(rowsAtInstant);

        return rowsAtInstant.Any(static x => x.AccountsForInstant);
    }

    /// <summary>
    /// Live-first ordering key (R3a). Ordering by <c>CreatedAt</c> alone lets an older terminal row mask a live one
    /// sharing the instant, which would report the wrong occurrence identity to the dispatcher.
    /// </summary>
    /// <param name="view">The projected row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="view" /> is <see langword="null" />.</exception>
    public static int LiveFirstRank(CronOccurrenceInstantView view)
    {
        Argument.IsNotNull(view);

        return view.IsLive ? 0 : 1;
    }

    private static class CompiledInstantViewProjector<TCronJob>
        where TCronJob : CronJobEntity
    {
        public static readonly Func<CronJobOccurrenceEntity<TCronJob>, CronOccurrenceInstantView> Value =
            InstantViewSelector<TCronJob>().Compile();
    }
}
