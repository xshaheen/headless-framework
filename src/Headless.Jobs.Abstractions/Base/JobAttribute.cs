// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Base;

/// <summary>
/// Declares a job class that the Jobs source generator registers in the assembly's generated <c>JobsModule</c>.
/// </summary>
/// <remarks>
/// Apply it to a non-abstract, non-generic <see langword="public"/> or <see langword="internal"/> top-level class that
/// implements exactly one of <see cref="IJob"/> or <see cref="IJob{TArgs}"/>. A module adds every job in its assembly
/// with <c>AddModule&lt;TAssemblyNamespace.JobsModule&gt;()</c>.
/// <para>
/// The attribute carries the job's intrinsic defaults. Everything it declares is checked at build time, so an invalid
/// identity, cron expression, or policy type fails the build rather than a run.
/// </para>
/// </remarks>
/// <param name="identity">
/// The durable job identity in <c>owner.name</c> form, for example <c>billing.close-day</c>. The first segment names
/// the owning module or service. The identity is persisted with every scheduled run, so renaming it orphans existing
/// rows.
/// </param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class JobAttribute(string identity) : Attribute
{
    /// <summary>The durable job identity in <c>owner.name</c> form.</summary>
    public string Identity { get; } = identity;

    /// <summary>
    /// Optional six-field (seconds-inclusive) cron expression that makes the job recurring, or <see langword="null"/>
    /// for a job that runs only when scheduled. A value starting with <c>%</c> names an <c>IConfiguration</c> key and
    /// is resolved and validated at startup instead of at build time.
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// IANA time zone the <see cref="Cron"/> expression is evaluated in, such as <c>Africa/Cairo</c>, or
    /// <see langword="null"/> for the scheduler's default zone. Ignored for jobs without a cron expression.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>Scheduling priority for the job's runs.</summary>
    public JobPriority Priority { get; set; } = JobPriority.Normal;

    /// <summary>
    /// Maximum number of concurrent runs on one node. <c>0</c> means unlimited within the scheduler's overall
    /// <c>MaxConcurrency</c> setting.
    /// </summary>
    public int MaxConcurrency { get; set; }

    /// <summary>Durable argument schema version, compared ordinally. Defaults to the initial schema version.</summary>
    public string ContractVersion { get; set; } = JobContract.InitialVersion;

    /// <summary>
    /// Failure policy type for this job. It must implement <c>Headless.Reliability.IFailurePolicy</c>; the source
    /// generator rejects any other type.
    /// </summary>
    /// <remarks>
    /// A policy set at the call site wins over this declaration, and this declaration wins over the host's default.
    /// Until a policy model is registered for the declared type, the job runs with the host's configured retry
    /// behavior.
    /// </remarks>
    public Type? Policy { get; set; }

    /// <summary>
    /// Recovery policy applied when this job's cron schedule falls behind. Ignored for jobs without a cron expression.
    /// </summary>
    /// <remarks>
    /// <b>Seeds the definition at creation only.</b> It is never reapplied when declared jobs are reconciled at
    /// startup, so a value later set through <c>ICronJobManager</c> stays in force across restarts and is an operator
    /// override by construction, which is why no provenance marker is persisted. Leave unset to take the
    /// scheduler-wide default.
    /// <para>
    /// Every comparable scheduler puts this knob on the mutable definition rather than in code: Hangfire has no
    /// attribute at all, and Quartz has attributes available yet deliberately placed misfire handling on the persisted
    /// trigger. This attribute declares an initial value; it is not the authority.
    /// </para>
    /// </remarks>
    public MissedRunPolicy OnMissedRun
    {
        get => _onMissedRun ?? MissedRunPolicy.Coalesce;
        set => _onMissedRun = value;
    }

    /// <summary>
    /// Seconds of lateness tolerated before a single pending occurrence counts as a misfire. Ignored for jobs without a
    /// cron expression.
    /// </summary>
    /// <remarks>
    /// Same seeding rule as <see cref="OnMissedRun"/>: creation only, never reapplied. Resolved once at creation from
    /// this value, then the scheduler-wide setting, then the framework default, and persisted on the definition so
    /// every node evaluates the same threshold. A locally configured value must never decide whether an instant
    /// misfired, or two nodes would disagree about the same tick.
    /// </remarks>
    public int MissedRunGraceSeconds
    {
        get => _missedRunGraceSeconds ?? JobsRecoveryDefaults.MissedRunGraceSeconds;
        set => _missedRunGraceSeconds = value;
    }

    /// <summary>
    /// Policy applied when an occurrence becomes due while an earlier occurrence of this job's cron definition is still
    /// unfinished. Ignored for jobs without a cron expression.
    /// </summary>
    /// <remarks>
    /// Same seeding rule as <see cref="OnMissedRun"/>: creation only, never reapplied, so a value later set through
    /// <c>ICronJobManager</c> stays in force. Leave unset to take the scheduler-wide default.
    /// </remarks>
    public CronOverlapPolicy OnOverlap
    {
        get => _onOverlap ?? CronOverlapPolicy.Allow;
        set => _onOverlap = value;
    }

    // Attribute arguments cannot be nullable value types, so "unset" is tracked separately from the public
    // non-nullable surface. The source generator reads these through the attribute's named arguments and emits only
    // the ones actually written, which lets an unset knob fall through to the scheduler-wide default rather than
    // silently pinning every definition to the framework default at creation.
    private MissedRunPolicy? _onMissedRun;
    private int? _missedRunGraceSeconds;
    private CronOverlapPolicy? _onOverlap;
}
