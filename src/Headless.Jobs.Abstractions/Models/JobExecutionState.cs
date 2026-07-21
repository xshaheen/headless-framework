// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// Mutable per-execution state carrier for a single job row (a time job or a cron occurrence). This is the SPI
/// value threaded from the scheduler through <c>IJobsDispatcher</c> into <c>IJobPersistenceProvider</c>: it
/// accumulates the outcome of an execution (status, elapsed time, exception, retry bookkeeping) and tracks
/// exactly which of its members changed via <see cref="PropertiesToUpdate"/> so a provider can issue a partial
/// update instead of rewriting the whole row. A provider implementation reads the mutated members named in
/// <see cref="PropertiesToUpdate"/> and persists only those.
/// </summary>
[PublicAPI]
public class JobExecutionState
{
    /// <summary>
    /// Cached execution delegate for this function, resolved once from the function registry and reused for the
    /// duration of the execution to avoid a per-call dictionary lookup.
    /// </summary>
    public JobFunctionDelegate CachedDelegate { get; set; } = null!;

    /// <summary>Cached scheduling priority for this function, mirrored from the function registry.</summary>
    public JobPriority CachedPriority { get; set; }

    /// <summary>Cached per-function maximum concurrency for this function, mirrored from the function registry.</summary>
    public int CachedMaxConcurrency { get; set; }

    /// <summary>Registered function name that identifies the job handler this state belongs to.</summary>
    public required string FunctionName { get; set; }

    /// <summary>Identifier of the job row (time job or cron occurrence) this state describes.</summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Identifier of the parent job when this row was produced as a child (e.g., a chained time job); otherwise
    /// <see langword="null"/>.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>Whether this row is a time job or a cron occurrence.</summary>
    public JobType Type { get; set; }

    /// <summary>Maximum number of retry attempts configured for this job.</summary>
    public int Retries { get; set; }

    /// <summary>Number of retry attempts already consumed for this job.</summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Tenant that owns the job row, restored around every execution attempt. <see langword="null"/> means the
    /// attempt runs system scope. Must be carried by every entity-to-state projection or pickup after restart
    /// silently drops the tenant.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>Current lifecycle status of the job row.</summary>
    public JobStatus Status { get; set; }

    /// <summary>Wall-clock duration of the execution, in milliseconds.</summary>
    public long ElapsedTime { get; set; }

    /// <summary>Captured exception text when the execution faulted; otherwise <see langword="null"/>.</summary>
    public string? ExceptionDetails { get; set; }

    /// <summary>UTC timestamp at which the execution ran.</summary>
    public DateTime ExecutedAt { get; set; }

    /// <summary>
    /// Per-attempt retry backoff intervals in seconds; <see langword="null"/> falls back to the scheduler default
    /// backoff.
    /// </summary>
    public int[]? RetryIntervals { get; set; }

    /// <summary>Whether the job's distributed lock should be released as part of persisting this state.</summary>
    public bool ReleaseLock { get; set; }

    // #1/#463 transient runtime flag (not persisted, not part of PropertiesToUpdate): set by the renewal loop when
    // it cancels the job on lease loss, so the cancellation handler leaves the row InProgress for the
    // stalled-reclaim / OnNodeDeath sweep instead of writing a terminal Cancelled (which would drop a still-valid
    // Retry job).

    /// <summary>
    /// Transient, non-persisted flag set by the lease-renewal loop when it cancels the job because the lease was
    /// lost. It signals the cancellation handler to leave the row <c>InProgress</c> for the stalled-reclaim /
    /// node-death sweep rather than writing a terminal <c>Cancelled</c> status. It is never part of
    /// <see cref="PropertiesToUpdate"/>.
    /// </summary>
    public bool LeaseLost { get; set; }

    /// <summary>The time the job was scheduled to run (UTC).</summary>
    public DateTime ExecutionTime { get; set; }

    /// <summary>The run condition that governs whether a queued occurrence is eligible to execute.</summary>
    public RunCondition RunCondition { get; set; }

    /// <summary>Execution state for child jobs produced by this job (e.g., chained time jobs).</summary>
    public List<JobExecutionState> TimeJobChildren { get; } = [];

    /// <summary>
    /// Names of the members mutated via <see cref="SetProperty{T}"/> since the last <see cref="ResetUpdateProps"/>.
    /// A persistence provider uses this set to issue a partial update touching only the changed columns.
    /// </summary>
    public HashSet<string> PropertiesToUpdate { get; } = [];

    /// <summary>
    /// Assigns <paramref name="value"/> to the property selected by <paramref name="property"/> and records the
    /// property name in <see cref="PropertiesToUpdate"/> so a provider persists only that change.
    /// </summary>
    /// <typeparam name="T">The property's value type.</typeparam>
    /// <param name="property">A member-access expression selecting the property to update (e.g., <c>x => x.Status</c>).</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>This same instance, to allow fluent chaining of updates.</returns>
    /// <exception cref="ArgumentException">The expression does not select a property.</exception>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(JobExecutionState))]
    public JobExecutionState SetProperty<T>(Expression<Func<JobExecutionState, T>> property, T value)
    {
        if (property.Body is MemberExpression { Member: PropertyInfo prop })
        {
            prop.SetValue(this, value);
            PropertiesToUpdate.Add(prop.Name);
        }
        else
        {
            throw new ArgumentException("Expression must point to a property", nameof(property));
        }

        return this;
    }

    /// <summary>
    /// Clears the tracked <see cref="PropertiesToUpdate"/> set, resetting partial-update tracking for reuse.
    /// </summary>
    /// <returns>This same instance, to allow fluent chaining.</returns>
    public JobExecutionState ResetUpdateProps()
    {
        PropertiesToUpdate.Clear();
        return this;
    }
}
