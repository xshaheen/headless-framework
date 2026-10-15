// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Represents an error that occurs when the coordination store refuses a registration: a newer incarnation of the same
/// node id was allocated before this registration was written, which means another process is registering with the
/// same <see cref="NodeId"/>, or the store lost the node's generation record (a reset or lossy failover) in between.
/// </summary>
/// <param name="identity">The node and incarnation identity whose registration was refused.</param>
[PublicAPI]
public sealed class MembershipRegistrationRejectedException(NodeIdentity identity)
    : CoordinationException(
        $"The coordination store refused the registration of '{identity}': either a newer incarnation of node id "
            + $"'{identity.NodeId}' was allocated first, because another process is registering with the same NodeId "
            + "(give every node a unique NodeId), or the store lost the node's generation record between allocation and "
            + "registration (a store reset or lossy failover)."
    )
{
    /// <summary>Gets the node and incarnation identity whose registration was refused.</summary>
    public NodeIdentity Identity { get; } = identity;
}
