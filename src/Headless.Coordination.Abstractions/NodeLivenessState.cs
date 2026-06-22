// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Store-classified liveness state for a current node incarnation.</summary>
[PublicAPI]
public enum NodeLivenessState
{
    /// <summary>The node has heartbeated within <see cref="CoordinationOptions.SuspicionThreshold"/>.</summary>
    Alive = 0,

    /// <summary>
    /// The last heartbeat is older than <see cref="CoordinationOptions.SuspicionThreshold"/> but younger
    /// than <see cref="CoordinationOptions.DeadThreshold"/>. The node may be slow, restarting, or
    /// partitioned.
    /// </summary>
    Suspected = 1,

    /// <summary>
    /// No heartbeat within <see cref="CoordinationOptions.DeadThreshold"/>. The incarnation is permanently
    /// ineligible for recovery; dead-owner reclaimers should clean up any resources it held.
    /// </summary>
    Dead = 2,
}
