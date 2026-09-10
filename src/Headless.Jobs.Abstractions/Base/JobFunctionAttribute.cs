// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Base;

/// <summary>
/// Marks a method as a job function that the source generator registers with the Jobs scheduler.
/// </summary>
/// <remarks>
/// Apply this attribute to a <see langword="public"/> or <see langword="internal"/> method on a non-nested,
/// non-abstract class. The source generator emits a <c>ModuleInitializer</c>-based registration (via
/// <c>JobFunctionProvider.RegisterFunctions</c>) that wires the method delegate into the scheduler at
/// application startup — no manual <c>AddJobsDiscovery</c> call is needed for each function.
/// <para>
/// The method may accept a <c>JobFunctionContext</c>, <c>JobFunctionContext&lt;T&gt;</c>, or
/// <c>CancellationToken</c> parameter, or have no parameters at all.
/// </para>
/// <para>
/// When <c>cronExpression</c> starts with <c>%</c> (e.g., <c>%Jobs:MyJob:Cron</c>) the value is
/// treated as a configuration key and resolved from <c>IConfiguration</c> at startup.
/// </para>
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public sealed class JobFunctionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="JobFunctionAttribute"/> for a time job or cron job.
    /// </summary>
    /// <param name="functionName">
    /// Unique name that identifies this function in the scheduler. Must match the name used when enqueuing
    /// the job via <c>ITimeJobManager</c> or <c>ICronJobManager</c>.
    /// </param>
    /// <param name="cronExpression">
    /// Optional six-field (seconds-inclusive) NCrontab expression. When non-null the function is registered
    /// as a recurring cron job. A value starting with <c>%</c> is resolved from <c>IConfiguration</c>.
    /// </param>
    /// <param name="taskPriority">Scheduling priority for the job; defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="maxConcurrency">
    /// Maximum number of concurrent executions. <c>0</c> (the default) means unlimited within the
    /// scheduler's overall <c>MaxConcurrency</c> setting.
    /// </param>
    public JobFunctionAttribute(
        string functionName,
        string? cronExpression = null,
        JobPriority taskPriority = JobPriority.Normal,
        int maxConcurrency = 0
    )
    {
        FunctionName = functionName;
        CronExpression = cronExpression;
        TaskPriority = taskPriority;
        MaxConcurrency = maxConcurrency;
    }

    /// <summary>
    /// Initializes a new <see cref="JobFunctionAttribute"/> for a time job without a cron expression.
    /// </summary>
    /// <param name="functionName">Unique name that identifies this function in the scheduler.</param>
    /// <param name="taskPriority">Scheduling priority for the job.</param>
    /// <param name="maxConcurrency">
    /// Maximum number of concurrent executions. <c>0</c> means unlimited within the scheduler's overall
    /// <c>MaxConcurrency</c> setting.
    /// </param>
    public JobFunctionAttribute(string functionName, JobPriority taskPriority, int maxConcurrency = 0)
    {
        FunctionName = functionName;
        TaskPriority = taskPriority;
        MaxConcurrency = maxConcurrency;
    }

    /// <summary>Unique name that identifies this function in the scheduler.</summary>
    public string FunctionName { get; }

    /// <summary>
    /// Optional six-field (seconds-inclusive) NCrontab expression, or <see langword="null"/> for a time job.
    /// A value starting with <c>%</c> is resolved from <c>IConfiguration</c> at startup.
    /// </summary>
    public string? CronExpression { get; }

    /// <summary>Scheduling priority for the job.</summary>
    public JobPriority TaskPriority { get; }

    /// <summary>
    /// Maximum number of concurrent executions. <c>0</c> means unlimited within the scheduler's overall
    /// <c>MaxConcurrency</c> setting.
    /// </summary>
    public int MaxConcurrency { get; }

    /// <summary>
    /// Recovery policy applied when this cron definition's schedule falls behind. Ignored for time jobs.
    /// </summary>
    /// <remarks>
    /// <b>Seeds the definition at creation only.</b> It is never reapplied when declared functions are reconciled at
    /// startup, so a value later set through <c>ICronJobManager</c> stays in force across restarts and is an operator
    /// override by construction — which is why no provenance marker is persisted. Leave unset to take the
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
    /// Seconds of lateness tolerated before a single pending occurrence counts as a misfire. Ignored for time jobs.
    /// </summary>
    /// <remarks>
    /// Same seeding rule as <see cref="OnMissedRun"/>: creation only, never reapplied. Resolved once at creation from
    /// this value, then the scheduler-wide setting, then the framework default, and persisted on the definition so
    /// every node evaluates the same threshold — a locally configured value must never decide whether an instant
    /// misfired, or two nodes would disagree about the same tick.
    /// </remarks>
    public int MissedRunGraceSeconds
    {
        get => _missedRunGraceSeconds ?? JobsRecoveryDefaults.MissedRunGraceSeconds;
        set => _missedRunGraceSeconds = value;
    }

    // Attribute arguments cannot be nullable value types, so "unset" is tracked separately from the public
    // non-nullable surface. The source generator reads these through the attribute's named arguments and emits only
    // the ones actually written, which is what lets an unset knob fall through to the scheduler-wide default rather
    // than silently pinning every definition to the framework default at creation.
    private MissedRunPolicy? _onMissedRun;
    private int? _missedRunGraceSeconds;
}
