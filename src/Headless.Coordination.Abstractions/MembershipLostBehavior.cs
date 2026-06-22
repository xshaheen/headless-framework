// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Behavior applied when the local process loses its own membership identity.</summary>
[PublicAPI]
public enum MembershipLostBehavior
{
    /// <summary>
    /// Triggers <c>IHostApplicationLifetime.StopApplication</c> so the process terminates and its
    /// container/supervisor can restart it with a fresh incarnation. This is the default and the
    /// recommended choice for stateful workloads where continued operation under a lost identity is unsafe
    /// (fail-stop semantics).
    /// </summary>
    StopApplication = 0,

    /// <summary>
    /// Cancels <see cref="INodeMembership.LocalMembershipLostToken"/> and raises the
    /// <see cref="LocalMembershipLost"/> event but does <em>not</em> stop the host process. Use this when
    /// the process can gracefully shed the coordination-dependent workload and remain running for other
    /// unrelated purposes.
    /// </summary>
    StopMembershipOnly = 1,
}
