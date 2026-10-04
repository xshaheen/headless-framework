// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Specifies the action taken when the local process loses its membership identity.</summary>
[PublicAPI]
public enum MembershipLostBehavior
{
    /// <summary>
    /// Stops the application host so the process terminates and the supervisor can restart it with a fresh incarnation.
    /// This is the default setting for fail-stop workloads where continued operation without valid membership is unsafe.
    /// </summary>
    StopApplication = 0,

    /// <summary>
    /// Cancels <see cref="INodeMembership.LocalMembershipLostToken"/> and emits the <see cref="LocalMembershipLost"/>
    /// event without stopping the host process. Use this setting when the process can shed coordination workloads
    /// while continuing independent operations.
    /// </summary>
    StopMembershipOnly = 1,
}
