// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Internal;

namespace Headless.Jobs;

/// <summary>
/// One seeded cron definition as the seed reconcile sees it: the row's identity and ownership marker, the job's
/// function and expression, plus the recovery and overlap settings to stamp on the definition <b>if it has to be
/// created</b>.
/// </summary>
/// <param name="Id">
/// Deterministic primary key of the seeded row, derived from <paramref name="Marker"/>. The reconcile matches stored
/// rows by this id, never by function, so a runtime row of the same function is never adopted, and concurrent seeds
/// converge on one row through primary-key dedup.
/// </param>
/// <param name="Marker">
/// Ownership marker stored on the row. It starts with the seed owner's prefix, so a reconcile deletes only rows its own
/// owner seeded, and it names the entry's source, so entries from different sources never share an id.
/// </param>
/// <param name="Function">Registered function name the definition runs.</param>
/// <param name="Expression">Six-field cron expression, already resolved from configuration when it was a <c>%</c> key.</param>
/// <param name="OnMissedRun">Recovery policy to seed at creation.</param>
/// <param name="MissedRunGraceSeconds">Misfire grace, in seconds, to seed at creation.</param>
/// <param name="OnOverlap">Overlap policy to seed at creation.</param>
/// <param name="EvaluationFingerprint">Current evaluator fingerprint stamped with a new or repositioned seed.</param>
/// <param name="ContractVersion">Registered payload version stamped only on newly created definitions.</param>
/// <param name="TimeZoneId">
/// IANA zone the expression is evaluated in, or <see langword="null"/> for the scheduler's default zone. Like the
/// expression it is part of the declared schedule, so a changed zone repositions an existing definition.
/// </param>
/// <param name="Retries">
/// Retry count to seed at creation, flattened from the job's resolved failure policy: its immediate plus its delayed
/// retries.
/// </param>
/// <param name="RetryIntervals">
/// Per-retry delays in whole seconds to seed at creation, or <see langword="null"/> when the policy does not retry:
/// <c>0</c> for each immediate retry, then each delayed retry's delay before jitter, rounded up.
/// </param>
/// <param name="Request">
/// Serialized request payload to store at creation, or <see langword="null"/> for a job that takes no arguments, which
/// stores an empty payload.
/// </param>
/// <param name="Description">Human-readable description to store at creation.</param>
/// <param name="OnNodeDeath">Node-death policy to store at creation.</param>
/// <param name="CorrelationId">Business correlation each occurrence captures, stored at creation.</param>
/// <param name="CausationId">Immediate business cause each occurrence captures, stored at creation.</param>
/// <remarks>
/// The recovery and overlap settings are already resolved by the caller — attribute value, else the scheduler-wide setting, else
/// the framework default — so the provider persists a concrete value rather than re-deriving one. That matters because
/// the threshold must be identical on every node: if each provider resolved it from local configuration, two nodes
/// could disagree about whether the same instant misfired.
/// <para>
/// They are applied <b>only when the definition is created</b> and never reapplied to an existing row. That single
/// rule is what makes a value later set through <c>ICronJobManager</c> an operator override by construction, with no
/// provenance marker to persist and no way for a redeploy to silently revert it.
/// </para>
/// <para>
/// Existing function/version/request tuples also remain unchanged. A new registration version cannot relabel
/// previously stored request bytes; changing an existing payload contract requires an explicit definition edit.
/// </para>
/// <para>
/// The retry settings follow the same creation-only rule, so a failure policy changed in code does not reach an existing
/// definition; edit the definition through <c>ICronJobManager</c> instead.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct CronSeedDefinition(
    Guid Id,
    string Marker,
    string Function,
    string Expression,
    MissedRunPolicy OnMissedRun,
    int MissedRunGraceSeconds,
    CronOverlapPolicy OnOverlap,
    string? EvaluationFingerprint = null,
    string ContractVersion = JobContract.InitialVersion,
    string? TimeZoneId = null,
    int Retries = 0,
    int[]? RetryIntervals = null,
    byte[]? Request = null,
    string? Description = null,
    NodeDeathPolicy OnNodeDeath = NodeDeathPolicy.Retry,
    string? CorrelationId = null,
    string? CausationId = null
)
{
    /// <summary>
    /// Builds the entry <paramref name="owner"/> seeds for the attribute cron of <paramref name="function"/>.
    /// </summary>
    internal static CronSeedDefinition ForCronFunction(
        string owner,
        string function,
        string expression,
        MissedRunPolicy onMissedRun,
        int missedRunGraceSeconds,
        CronOverlapPolicy onOverlap,
        string? evaluationFingerprint = null,
        string contractVersion = JobContract.InitialVersion,
        string? timeZoneId = null,
        int retries = 0,
        int[]? retryIntervals = null
    )
    {
        return new(
            JobsSeedId.ForCronSeed(owner, function),
            JobsSeedId.CronSeedMarker(owner, function),
            function,
            expression,
            onMissedRun,
            missedRunGraceSeconds,
            onOverlap,
            evaluationFingerprint,
            contractVersion,
            timeZoneId,
            retries,
            retryIntervals
        );
    }

    /// <summary>
    /// Builds the entry <paramref name="owner"/> seeds for the recurring-job declaration keyed <paramref name="key"/>,
    /// which runs <paramref name="function"/>. Several declarations may run one function; each key is its own row.
    /// </summary>
    internal static CronSeedDefinition ForDeclaration(
        string owner,
        string key,
        string function,
        string expression,
        MissedRunPolicy onMissedRun,
        int missedRunGraceSeconds,
        CronOverlapPolicy onOverlap,
        string? evaluationFingerprint = null,
        string contractVersion = JobContract.InitialVersion,
        string? timeZoneId = null,
        int retries = 0,
        int[]? retryIntervals = null,
        byte[]? request = null,
        string? description = null,
        NodeDeathPolicy onNodeDeath = NodeDeathPolicy.Retry,
        string? correlationId = null,
        string? causationId = null
    )
    {
        return new(
            JobsSeedId.ForDeclaration(owner, key),
            JobsSeedId.DeclarationSeedMarker(owner, key),
            function,
            expression,
            onMissedRun,
            missedRunGraceSeconds,
            onOverlap,
            evaluationFingerprint,
            contractVersion,
            timeZoneId,
            retries,
            retryIntervals,
            request,
            description,
            onNodeDeath,
            correlationId,
            causationId
        );
    }
}
