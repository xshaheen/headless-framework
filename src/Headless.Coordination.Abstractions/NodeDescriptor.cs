// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Describes node registration metadata written to the backing store when a node incarnation joins the cluster.
/// Written during <see cref="INodeMembership.RegisterAsync"/> and not updated on subsequent heartbeats.
/// </summary>
[PublicAPI]
public sealed record NodeDescriptor
{
    /// <summary>Gets the node and incarnation identity for this descriptor.</summary>
    public required NodeIdentity Identity { get; init; }

    /// <summary>Gets the hostname of the process.</summary>
    public string? HostName { get; init; }

    /// <summary>
    /// Gets named service endpoints exposed by this node, keyed by endpoint name.
    /// Values are typically addresses, host and port pairs, or URI strings.
    /// </summary>
    public IReadOnlyDictionary<string, string> Endpoints { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the optional role label for this node. Informational only; the system does not enforce topology based on roles.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Gets key and value metadata pairs attached to this node, copied from <see cref="CoordinationOptions.Metadata"/>
    /// at registration time.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
