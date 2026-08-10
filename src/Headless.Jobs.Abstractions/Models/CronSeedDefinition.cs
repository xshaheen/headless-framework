// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// One code-declared cron function as startup reconciliation sees it: the durable name and expression, plus the
/// recovery settings to stamp on the definition <b>if it has to be created</b>.
/// </summary>
/// <param name="Function">Unique function name; also the seed row's deterministic identity.</param>
/// <param name="Expression">Six-field cron expression, already resolved from configuration when it was a <c>%</c> key.</param>
/// <param name="OnMissedRun">Recovery policy to seed at creation.</param>
/// <param name="MissedRunGraceSeconds">Misfire grace, in seconds, to seed at creation.</param>
/// <param name="EvaluationFingerprint">Current evaluator fingerprint stamped with a new or repositioned seed.</param>
/// <remarks>
/// Both recovery settings are already resolved by the caller — attribute value, else the scheduler-wide setting, else
/// the framework default — so the provider persists a concrete value rather than re-deriving one. That matters because
/// the threshold must be identical on every node: if each provider resolved it from local configuration, two nodes
/// could disagree about whether the same instant misfired.
/// <para>
/// They are applied <b>only when the definition is created</b> and never reapplied to an existing row. That single
/// rule is what makes a value later set through <c>ICronJobManager</c> an operator override by construction, with no
/// provenance marker to persist and no way for a redeploy to silently revert it.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct CronSeedDefinition(
    string Function,
    string Expression,
    MissedRunPolicy OnMissedRun,
    int MissedRunGraceSeconds,
    string? EvaluationFingerprint = null
);
