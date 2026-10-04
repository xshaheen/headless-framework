// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Represents an error that occurs when local ownership-sensitive work continues after membership loss is observed.</summary>
/// <param name="identity">The lost node and incarnation identity.</param>
[PublicAPI]
public sealed class MembershipLostException(NodeIdentity identity)
    : CoordinationException($"Local membership identity '{identity}' has been lost.")
{
    /// <summary>Gets the lost node and incarnation identity.</summary>
    public NodeIdentity Identity { get; } = identity;
}
