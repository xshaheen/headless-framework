// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>
/// Builds the owner tag a consumer stamps on the rows a node holds: <c>cluster/node@incarnation</c>, such as
/// <c>orders/pod-a@3</c>.
/// </summary>
/// <remarks>
/// Membership, and therefore liveness, is scoped to one cluster, while the rows a node stamps can live in a store that
/// several clusters share (one Jobs store or one messaging table used by several apps). A bare
/// <c>node@incarnation</c> means different nodes in different clusters, so a cluster that judged it dead, or absent
/// from its snapshot, would reclaim another cluster's live work. The cluster prefix keeps every comparison inside the
/// cluster whose snapshot made the decision. The cluster name cannot contain <see cref="ClusterSeparator"/>, so the
/// first separator always ends the cluster name.
/// </remarks>
[PublicAPI]
public static class NodeOwnerTag
{
    /// <summary>The character that separates the cluster name from the node identity.</summary>
    public const char ClusterSeparator = '/';

    /// <summary>
    /// Gets the maximum owner tag length in characters: a <see cref="CoordinationOptions.ClusterNameMaxLength"/> cluster
    /// name, the separator, a <see cref="NodeId.MaxLength"/> node identifier, <c>@</c>, and a 19-digit incarnation.
    /// Size a column that stores owner tags to at least this length.
    /// </summary>
    public const int MaxLength = CoordinationOptions.ClusterNameMaxLength + 1 + NodeId.MaxLength + 1 + 19;

    /// <summary>Formats the owner tag of <paramref name="identity"/> in the cluster <paramref name="clusterName"/>.</summary>
    /// <param name="clusterName">The coordination cluster the identity belongs to.</param>
    /// <param name="identity">The node identity.</param>
    /// <returns>The owner tag, formatted as <c>cluster/node@incarnation</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clusterName"/> is <see langword="null"/>.</exception>
    public static string Format(string clusterName, NodeIdentity identity)
    {
        Argument.IsNotNull(clusterName);

        return string.Concat(clusterName, ClusterSeparator.ToString(), identity.ToString());
    }

    /// <summary>
    /// Returns whether <paramref name="ownerTag"/> was issued in the cluster <paramref name="clusterName"/>. Use it to
    /// limit a decision taken from one cluster's liveness snapshot to the owners that snapshot can speak for.
    /// </summary>
    /// <param name="ownerTag">An owner tag read from a store.</param>
    /// <param name="clusterName">The coordination cluster to test against.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="ownerTag"/> starts with <paramref name="clusterName"/> followed by
    /// <see cref="ClusterSeparator"/>; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool IsInCluster(string ownerTag, string clusterName)
    {
        Argument.IsNotNull(ownerTag);
        Argument.IsNotNull(clusterName);

        return ownerTag.Length > clusterName.Length
            && ownerTag[clusterName.Length] == ClusterSeparator
            && ownerTag.StartsWith(clusterName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the owner tag of the local node, or <see langword="null"/> when <see cref="INodeMembership.Identity"/> is
    /// <see langword="null"/> (not registered, left, lost, or past its self-fence deadline).
    /// </summary>
    /// <param name="membership">The local node membership.</param>
    /// <returns>The local owner tag, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="membership"/> is <see langword="null"/>.</exception>
    public static string? GetOwnerTag(this INodeMembership membership)
    {
        Argument.IsNotNull(membership);

        return membership.Identity is { } identity ? Format(membership.ClusterName, identity) : null;
    }
}
