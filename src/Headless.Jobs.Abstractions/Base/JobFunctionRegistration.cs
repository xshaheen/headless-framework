// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Named registration for a single <c>[Job]</c> class. Carries the per-job scheduling knobs the source
/// generator emits at build time and the scheduler reads at dispatch time. This type is the ABI between the
/// generated per-assembly <c>JobsModule</c> in every consuming assembly and the
/// per-host job registry in <c>Headless.Jobs.Core</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Additive-only evolution policy.</b> This record struct is baked into every consumer assembly's compiled
/// <c>JobsModule</c>, so its shape is a binary contract. New per-function knobs must be added ONLY as new
/// optional <see langword="init"/> members carrying a safe default — never reorder, rename, or remove existing members, never
/// tighten an existing member to <see langword="required"/>, and never convert this to a positional record. Because the
/// generator (and every hand-written registration) constructs it via an object initializer, adding an optional
/// member keeps both already-compiled consumers and consumer source compiling against a newer runtime. The four
/// members below are the current knobs and are <see langword="required"/>; future additions are the ones that must stay
/// optional.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct JobFunctionRegistration
{
    /// <summary>
    /// The six-field NCrontab expression that schedules this function, or <see cref="string.Empty"/> for a time
    /// job (dispatched on demand rather than on a cron cadence). A value beginning with <c>%</c> names an
    /// <c>IConfiguration</c> key that is resolved to the effective expression at application startup.
    /// </summary>
    public required string CronExpression { get; init; }

    /// <summary>Scheduling priority applied when this function is queued onto the Jobs thread pool.</summary>
    public required JobPriority Priority { get; init; }

    /// <summary>
    /// The generated execution delegate that constructs the job class from the run's scope and invokes its
    /// <c>ExecuteAsync</c>.
    /// </summary>
    public required JobFunctionDelegate Delegate { get; init; }

    /// <summary>
    /// Maximum number of concurrent in-flight executions allowed for this function on the node; <c>0</c> means
    /// unbounded (governed only by the global scheduler concurrency limit).
    /// </summary>
    public required int MaxConcurrency { get; init; }

    /// <summary>
    /// Recovery policy to seed onto this function's cron definition when it is first created, or
    /// <see langword="null"/> to take the scheduler-wide default. Ignored for time jobs.
    /// </summary>
    /// <remarks>
    /// Optional by the additive-only policy above: a consumer assembly compiled before this member existed still
    /// registers successfully and reads as "unset". Seeds at creation only and is never reapplied during startup
    /// reconciliation, so a value later set through <c>ICronJobManager</c> stays in force.
    /// </remarks>
    public MissedRunPolicy? OnMissedRun { get; init; }

    /// <summary>
    /// Misfire grace in seconds to seed onto this function's cron definition when it is first created, or
    /// <see langword="null"/> to take the scheduler-wide default. Ignored for time jobs.
    /// </summary>
    /// <remarks>Same optionality and creation-only seeding rule as <see cref="OnMissedRun"/>.</remarks>
    public int? MissedRunGraceSeconds { get; init; }

    /// <summary>
    /// Overlap policy to seed onto this function's cron definition when it is first created, or
    /// <see langword="null"/> to take the scheduler-wide default. Ignored for time jobs.
    /// </summary>
    /// <remarks>Same optionality and creation-only seeding rule as <see cref="OnMissedRun"/>.</remarks>
    public CronOverlapPolicy? OnOverlap { get; init; }

    /// <summary>
    /// The <c>[Job]</c> class this registration runs, or <see langword="null"/> for a hand-written registration. The
    /// scheduler resolves <c>EnqueueAsync&lt;TJob&gt;()</c> and its siblings through it.
    /// </summary>
    public Type? JobType { get; init; }

    /// <summary>
    /// IANA time zone the cron expression is evaluated in when the definition is seeded, or <see langword="null"/>
    /// for the scheduler's default zone. Ignored for time jobs.
    /// </summary>
    public string? TimeZoneId { get; init; }

    /// <summary>
    /// Creates the failure policy the <c>[Job]</c> attribute declares, or <see langword="null"/> when it declares none
    /// and the host's default policy applies.
    /// </summary>
    /// <remarks>
    /// The generator emits <c>static () =&gt; new global::MyPolicy()</c> so the policy is created without reflection.
    /// The host calls it once while it builds its job registry and caches the built definition; a factory that returns
    /// <see langword="null"/> or an invalid policy fails startup. Optional by the additive-only policy above.
    /// </remarks>
    public Func<Reliability.FailurePolicy>? FailurePolicy { get; init; }
}
