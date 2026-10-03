// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs;

/// <summary>
/// The single occupied-instant accounting rule, shared by every provider and by both the materialization and
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
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CronOccurrenceInstantView))]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "The member bindings target CronOccurrenceInstantView's public properties, which the DynamicDependency above keeps, so the property metadata Expression.Bind resolves is never trimmed."
    )]
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
