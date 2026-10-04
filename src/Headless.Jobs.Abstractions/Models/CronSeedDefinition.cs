// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// One code-declared cron function as startup reconciliation sees it: the durable name and expression, plus the
/// recovery and overlap settings to stamp on the definition <b>if it has to be created</b>.
/// </summary>
/// <param name="Function">Unique function name; also the seed row's deterministic identity.</param>
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
    string Function,
    string Expression,
    MissedRunPolicy OnMissedRun,
    int MissedRunGraceSeconds,
    CronOverlapPolicy OnOverlap,
    string? EvaluationFingerprint = null,
    string ContractVersion = JobContract.InitialVersion,
    string? TimeZoneId = null,
    int Retries = 0,
    int[]? RetryIntervals = null
);
