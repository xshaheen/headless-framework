// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Specifies the action taken when the local process loses its membership identity.</summary>
[PublicAPI]
public enum MembershipLostBehavior
{
    /// <summary>
    /// Stops the application host through <c>IHostApplicationLifetime.StopApplication</c>, so the process
    /// terminates and its container or supervisor restarts it with a fresh incarnation. This is the default
    /// and the recommended choice for stateful workloads where continued operation under a lost identity is
    /// unsafe (fail-stop semantics).
    /// </summary>
    StopApplication = 0,

    /// <summary>
    /// Cancels <see cref="INodeMembership.LocalMembershipLostToken"/> and emits the <see cref="LocalMembershipLost"/>
    /// event, without stopping the host process. Use this setting when the process can gracefully shed its
    /// coordination-dependent workload and remain running for unrelated purposes.
    /// </summary>
    StopMembershipOnly = 1,
}
