// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;

namespace Tests.Cluster;

/// <summary>
/// The membership and lease timings every cluster node runs on. They follow the messaging chaos precedent (a 2-second
/// heartbeat, suspicion at three beats, dead no sooner than the lease) instead of the harness's 200 ms profile, which
/// self-fences live nodes once a loaded runner stalls one beat. Every interval the oracle sizes from is set here
/// explicitly, so a framework default that moves cannot silently change what the oracle allows.
/// </summary>
public static class ClusterTimings
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan SuspicionThreshold = HeartbeatInterval * 3;

    /// <summary>No lower than the lease: a node declared dead has let every lease it held run out.</summary>
    public static readonly TimeSpan DeadThreshold = TimeSpan.FromSeconds(12);

    public static readonly TimeSpan DeadRetentionWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The margin coordination derives when none is set: the smaller of 5 seconds and a sixth of the dead threshold.
    /// The nodes leave it unset, so they fence where production nodes would.
    /// </summary>
    public static readonly TimeSpan SelfFenceMargin = TimeSpan.FromTicks(
        Math.Min(TimeSpan.FromSeconds(5).Ticks, DeadThreshold.Ticks / 6)
    );

    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(12);

    /// <summary>The dead-node reconcile and orphan sweep cadence, set below its half-retention ceiling.</summary>
    public static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(5);

    /// <summary>The fallback poll, well under the lease so a lapsed claim is picked up within it.</summary>
    public static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(2);

    /// <summary>The graceful stop budget, the same for the host and the Jobs scheduler as the scheduler asks.</summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How often the orphan sampler reads the store and the liveness snapshot.</summary>
    public static readonly TimeSpan SamplePeriod = TimeSpan.FromSeconds(2);

    public const int MaxConcurrencyPerNode = 8;

    /// <summary>How long a row may stay owned by a non-live owner: one lease, two reconciles, and one sampling gap.</summary>
    public static TimeSpan OrphanBound => LeaseDuration + (ReconcileInterval * 2) + SamplePeriod;

    /// <summary>A freeze outlasts the lease, so the frozen node's leases lapse and its peers recover the rows.</summary>
    public static TimeSpan FreezeLength => LeaseDuration + TimeSpan.FromSeconds(4);

    public static void Configure(CoordinationOptions options, string clusterName)
    {
        options.ClusterName = clusterName;
        options.HeartbeatInterval = HeartbeatInterval;
        options.SuspicionThreshold = SuspicionThreshold;
        options.DeadThreshold = DeadThreshold;
        options.DeadRetentionWindow = DeadRetentionWindow;
        // A fenced node keeps running, so the scenarios see that Jobs itself stops claiming and renewing on it; the
        // cluster replaces such a node once its fault ends, because a crossed self-fence is terminal.
        options.MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly;
    }

    public static void Configure(SchedulerOptionsBuilder scheduler)
    {
        scheduler.LeaseDuration = LeaseDuration;
        scheduler.FallbackIntervalChecker = FallbackInterval;
        scheduler.DeadNodeReconcileInterval = ReconcileInterval;
        scheduler.ShutdownTimeout = ShutdownTimeout;
        scheduler.MaxConcurrency = MaxConcurrencyPerNode;
    }
}
