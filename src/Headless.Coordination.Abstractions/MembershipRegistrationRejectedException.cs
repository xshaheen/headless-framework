// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Represents an error that occurs when the coordination store refuses a registration because a newer incarnation of
/// the same node id was allocated before this registration was written: another process is registering with the same
/// <see cref="NodeId"/>.
/// </summary>
/// <param name="identity">The node and incarnation identity whose registration was refused.</param>
[PublicAPI]
public sealed class MembershipRegistrationRejectedException(NodeIdentity identity)
    : CoordinationException(
        $"The coordination store refused the registration of '{identity}' because a newer incarnation of node id "
            + $"'{identity.NodeId}' was allocated first. Another process is registering with the same NodeId; give "
            + "every node a unique NodeId."
    )
{
    /// <summary>Gets the node and incarnation identity whose registration was refused.</summary>
    public NodeIdentity Identity { get; } = identity;
}
