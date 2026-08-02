// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Named registration for a single <c>[JobFunction]</c>. Carries the per-function scheduling knobs the source
/// generator emits at build time and the scheduler reads at dispatch time. This type is the ABI between the
/// generated per-assembly <c>[ModuleInitializer]</c> in every consuming assembly and the
/// <c>JobFunctionProvider</c> registry in <c>Headless.Jobs.Core</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Additive-only evolution policy.</b> This record struct is baked into every consumer assembly's compiled
/// <c>[ModuleInitializer]</c>, so its shape is a binary contract. New per-function knobs must be added ONLY as new
/// optional <c>init</c> members carrying a safe default — never reorder, rename, or remove existing members, never
/// tighten an existing member to <c>required</c>, and never convert this to a positional record. Because the
/// generator (and every hand-written registration) constructs it via an object initializer, adding an optional
/// member keeps both already-compiled consumers and consumer source compiling against a newer runtime. The four
/// members below are the current knobs and are <c>required</c>; future additions are the ones that must stay
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
    /// The generated execution delegate that resolves the job class from DI and invokes its
    /// <c>[JobFunction]</c>-annotated method.
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
}
