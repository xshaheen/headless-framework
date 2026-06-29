// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Coordination;

/// <summary>Thrown when local ownership-sensitive work continues after membership loss is observed.</summary>
[PublicAPI]
public sealed class MembershipLostException(NodeIdentity identity)
    : CoordinationException($"Local membership identity '{identity}' has been lost.")
{
    /// <summary>The lost <c>node@incarnation</c> identity.</summary>
    public NodeIdentity Identity { get; } = identity;
}
