// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Fine-grained configuration for the Jobs scheduler: node identity, thread pool, lease duration,
/// renewal cadence, and start mode.
/// </summary>
public sealed class SchedulerOptionsBuilder
{
    private int? _maxLongRunningConcurrency;

    /// <summary>
    /// Identifies this node on the in-memory single-process path; defaults to <see cref="Environment.MachineName"/>.
    /// The durable (Coordination) path does NOT use this value — it stamps rows with the membership
    /// <c>node@incarnation</c> owner, and node identity there (including K8s pod-collision handling via
    /// <c>POD_NAME</c>) is owned by <c>Headless.Coordination</c>'s node-id provider, not this option. This value
    /// only serves as the durable path's pre-registration display fallback.
    /// </summary>
    public string NodeId { get; set; } = Environment.MachineName;

    /// <summary>
    /// Maximum number of jobs that may execute concurrently across all priorities. Defaults to
    /// <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum number of dedicated threads used by <see cref="Enums.JobPriority.LongRunning"/> jobs. When not set,
    /// defaults to the smaller of <see cref="MaxConcurrency"/> and four.
    /// </summary>
    public int MaxLongRunningConcurrency
    {
        get => _maxLongRunningConcurrency ?? Math.Min(MaxConcurrency, 4);
        set => _maxLongRunningConcurrency = value;
    }

    /// <summary>
    /// How long an idle worker thread waits before terminating. Defaults to one minute.
    /// </summary>
    public TimeSpan IdleWorkerTimeOut { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Recovery policy seeded onto cron definitions created without one on their <c>[Job]</c> attribute.
    /// Defaults to <see cref="MissedRunPolicy.Coalesce"/>.
    /// </summary>
    /// <remarks>
    /// Read once when a definition is created and persisted on it. Changing this later does not retroactively alter
    /// existing definitions -- deliberately, so a configuration edit on one node can never silently change how another
    /// node recovers a schedule that is already running.
    /// </remarks>
    public MissedRunPolicy DefaultMissedRunPolicy { get; set; } = MissedRunPolicy.Coalesce;

    /// <summary>
    /// How often the evaluation-fingerprint sweep looks for definitions whose schedule-interpretation rules changed.
    /// Defaults to one hour.
    /// </summary>
    /// <remarks>
    /// Rules change on the timescale of an OS package update, not a scheduler tick, so this is deliberately long. The
    /// sweep also runs once at startup, which is when a fingerprint is most likely to have gone stale.
    /// </remarks>
    public TimeSpan FingerprintSweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Maximum definitions the fingerprint sweep rebases per pass. Defaults to 100.</summary>
    /// <remarks>
    /// A tzdata update invalidates every definition in the affected zone at once, so the first sweep after one can
    /// have a large working set. Bounding each pass keeps that from becoming one long transaction-heavy burst;
    /// the remainder is picked up on the next pass.
    /// </remarks>
    public int FingerprintSweepBatchSize { get; set; } = 100;

    /// <summary>
    /// Misfire grace, in seconds, seeded onto cron definitions created without one on their <c>[Job]</c>
    /// attribute. Defaults to <see cref="JobsRecoveryDefaults.MissedRunGraceSeconds"/>.
    /// </summary>
    /// <remarks>
    /// Resolved once at creation and persisted on the definition so every node evaluates the same threshold for it.
    /// Values must be greater than zero; startup validation rejects an invalid value because a zero threshold would
    /// classify every tick delayed by a garbage collection as a misfire.
    /// </remarks>
    public int DefaultMissedRunGraceSeconds { get; set; } = JobsRecoveryDefaults.MissedRunGraceSeconds;

    /// <summary>
    /// Overlap policy seeded onto cron definitions created without one on their <c>[Job]</c> attribute.
    /// Defaults to <see cref="CronOverlapPolicy.Allow"/>.
    /// </summary>
    /// <remarks>
    /// Same creation-only rule as <see cref="DefaultMissedRunPolicy"/>: changing it later does not alter existing
    /// definitions.
    /// </remarks>
    public CronOverlapPolicy DefaultOverlapPolicy { get; set; } = CronOverlapPolicy.Allow;

    /// <summary>
    /// How long a per-row pickup lease is held before it expires and the row becomes re-claimable. Stamped as
    /// <c>LockedUntil = now + LeaseDuration</c> on every claim. In-memory storage uses the injected
    /// <see cref="TimeProvider"/>; relational storage translates the claim expression to the database UTC clock so
    /// stamping and lease-expiry comparison share one authority without a separate clock query.
    /// <para>
    /// Running jobs slide this lease forward on the <see cref="LeaseRenewalInterval"/> cadence (#316), so
    /// <c>LeaseDuration</c> no longer needs to exceed the longest job runtime — a healthy long job keeps renewing.
    /// It now sizes two things: the <c>Idle</c>/<c>Queued</c> claim→start window (a row claimed but not yet started
    /// can lapse and be re-claimed — keep it ≥ <see cref="FallbackIntervalChecker"/>), and the recovery latency for
    /// a stalled running job (a job that stops renewing is reclaimed within ≈ one <c>LeaseDuration</c>). Defaults to
    /// five minutes.
    /// </para>
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often a running job's lease is renewed (slides <c>LockedUntil</c> forward) while it executes, so a
    /// healthy long-running job is never falsely reclaimed (#316). The owning worker's execution loop extends the
    /// lease on this cadence; a job that stops renewing (crashed or wedged) has its lease lapse and is reclaimed
    /// per its <c>OnNodeDeath</c> policy. <see langword="null"/> (the default) derives ≈ <see cref="LeaseDuration"/> / 3 so a
    /// single missed renewal cannot lapse the lease. An explicit value must be positive and strictly less than
    /// <see cref="LeaseDuration"/>; see <see cref="ResolveLeaseRenewalInterval"/>.
    /// </summary>
    public TimeSpan? LeaseRenewalInterval { get; set; }

    /// <summary>
    /// How often a running time job checks its durable cancellation request. <see langword="null"/> uses the
    /// effective <see cref="LeaseRenewalInterval"/> cadence. The effective interval must be finite, positive, and no
    /// greater than <see cref="LeaseDuration"/>.
    /// </summary>
    public TimeSpan? CancellationObservationInterval { get; set; }

    /// <summary>
    /// How often the fallback background service wakes up to poll for due jobs and reclaim stalled
    /// leases when no scheduler event triggered an earlier wake-up. Defaults to 30 seconds. Should be
    /// less than or equal to <see cref="LeaseDuration"/> so a stalled job is reclaimed within one
    /// lease TTL.
    /// </summary>
    public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The timezone used when evaluating cron expressions and interpreting <c>Kind=Unspecified</c> execution
    /// times. Defaults to UTC. Never defaulted to <see cref="TimeZoneInfo.Local"/>: two fleet nodes with
    /// different container timezones would evaluate one cron expression to two different UTC instants, and the
    /// occurrence dedup unique index cannot collapse them — every tick would run once per distinct timezone.
    /// Set an explicit zone only when every node in the fleet is configured identically.
    /// </summary>
    public TimeZoneInfo SchedulerTimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// How often the durable path reconciles dead nodes from the membership liveness snapshot to reclaim
    /// any <c>NodeLeft</c> signal missed while not subscribed. Membership events accelerate recovery; this
    /// periodic reconcile is the backstop (origin §4b invariant). Defaults to one minute.
    /// </summary>
    public TimeSpan DeadNodeReconcileInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Controls how job processing starts. Defaults to <see cref="JobsStartMode.Immediate"/>.
    /// </summary>
    public JobsStartMode StartMode { get; set; } = JobsStartMode.Immediate;

    /// <summary>The default value for <see cref="MaxChainDepth"/> (10).</summary>
    public const int DefaultMaxChainDepth = 10;

    /// <summary>
    /// The maximum number of nodes allowed on any single root-to-leaf path of an enqueued <c>JobChain</c>
    /// (on-success and on-failure edges both count). Enforced by <c>IJobScheduler.EnqueueAsync(JobChain, …)</c>
    /// before persistence — an over-deep chain is rejected naming this limit. Must be at least <c>1</c> and no more
    /// than <c>JobChain.MaxStructuralDepth</c> (the structural ceiling <c>JobChainBuilder.Build()</c> enforces); the
    /// registration guard rejects an out-of-range value so the two limits can never contradict. Defaults to
    /// <see cref="DefaultMaxChainDepth"/>.
    /// </summary>
    public int MaxChainDepth { get; set; } = DefaultMaxChainDepth;

    /// <summary>
    /// Whether the Jobs-scoped distributed lock coarse-gates startup cron-seed migration. Enabled only via the
    /// <c>UseDistributedLock(...)</c> builder extension (the setter is internal so the flag can never be
    /// <see langword="true"/> while the keyed slot still holds the <c>NullDistributedLock</c> fallback — that would
    /// silently no-op the guard on every node with no diagnostic). Defaults to <see langword="false"/> (no lock —
    /// every node runs the seed independently; seeded rows carry a deterministic primary key, so simultaneous
    /// first-boot still converges on a single row without this gate). This is an optimization flag, never the
    /// job-execution correctness boundary.
    /// </summary>
    public bool UseStorageLock { get; internal set; }

    /// <summary>
    /// Resolves the effective lease-renewal cadence (#316): an explicit <see cref="LeaseRenewalInterval"/> when
    /// set, otherwise the derived ≈ <see cref="LeaseDuration"/> / 3. Validates an explicit value — it must be
    /// positive and strictly less than <see cref="LeaseDuration"/>, so a renewal always lands before the lease
    /// deadline. Throws <see cref="InvalidOperationException"/> on a misconfigured explicit value; the startup
    /// initialization service calls this once (ValidateOnStart-equivalent), and the execution handler calls it to
    /// read the cadence.
    /// </summary>
    internal TimeSpan ResolveLeaseRenewalInterval()
    {
        if (LeaseRenewalInterval is not { } interval)
        {
            return TimeSpan.FromTicks(LeaseDuration.Ticks / 3);
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"SchedulerOptionsBuilder.LeaseRenewalInterval ({interval}) must be positive."
            );
        }

        if (interval >= LeaseDuration)
        {
            throw new InvalidOperationException(
                $"SchedulerOptionsBuilder.LeaseRenewalInterval ({interval}) must be strictly less than "
                    + $"LeaseDuration ({LeaseDuration}) so a renewal lands before the lease deadline."
            );
        }

        return interval;
    }

    /// <summary>Resolves and validates the durable-cancellation observation cadence.</summary>
    internal TimeSpan ResolveCancellationObservationInterval()
    {
        var interval = CancellationObservationInterval ?? ResolveLeaseRenewalInterval();
        if (interval <= TimeSpan.Zero || interval == Timeout.InfiniteTimeSpan)
        {
            throw new InvalidOperationException(
                $"SchedulerOptionsBuilder.CancellationObservationInterval ({interval}) must be finite and positive."
            );
        }

        if (interval > LeaseDuration)
        {
            throw new InvalidOperationException(
                $"SchedulerOptionsBuilder.CancellationObservationInterval ({interval}) must not exceed "
                    + $"LeaseDuration ({LeaseDuration})."
            );
        }

        return interval;
    }
}
