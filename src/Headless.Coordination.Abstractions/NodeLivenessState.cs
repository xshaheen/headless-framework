// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Specifies the store-classified liveness state for a node incarnation.</summary>
/// <remarks>
/// Additional members may be added in future versions. Consumers switching on this enum should include a
/// default branch and treat unrecognized values as <see cref="Suspected"/> rather than <see cref="Alive"/>.
/// </remarks>
[PublicAPI]
public enum NodeLivenessState
{
    /// <summary>The node recorded a heartbeat within <see cref="CoordinationOptions.SuspicionThreshold"/>.</summary>
    Alive = 0,

    /// <summary>
    /// The last heartbeat is older than <see cref="CoordinationOptions.SuspicionThreshold"/> but younger
    /// than <see cref="CoordinationOptions.DeadThreshold"/>. The node may be slow, restarting, or partitioned.
    /// </summary>
    Suspected = 1,

    /// <summary>
    /// No heartbeat was recorded within <see cref="CoordinationOptions.DeadThreshold"/>. The incarnation is permanently
    /// ineligible for recovery, and dead-owner reclaimers clean up resources held by it.
    /// </summary>
    Dead = 2,
}
