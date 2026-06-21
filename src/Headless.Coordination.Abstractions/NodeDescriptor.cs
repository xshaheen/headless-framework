// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Cold descriptor written to the backing store when a node incarnation joins a coordination cluster.
/// Updated on each <see cref="INodeMembership.RegisterAsync"/> call but not on subsequent heartbeats.
/// </summary>
[PublicAPI]
public sealed record NodeDescriptor
{
    /// <summary>The <c>node@incarnation</c> identity this descriptor belongs to.</summary>
    public required NodeIdentity Identity { get; init; }

    /// <summary>DNS or machine hostname of the process, if available.</summary>
    public string? HostName { get; init; }

    /// <summary>
    /// Named service endpoints exposed by this node (for example <c>grpc</c>, <c>http</c>), keyed by
    /// endpoint name. Values are typically host:port or URI strings. Empty by default.
    /// </summary>
    public IReadOnlyDictionary<string, string> Endpoints { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Optional role label for this node. Informational only; the system does not enforce topology based
    /// on roles.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Arbitrary key/value metadata attached to this node, sourced from
    /// <see cref="CoordinationOptions.Metadata"/> at registration time.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
