// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>Compares one metadata value across the live incarnations of a liveness snapshot.</summary>
/// <remarks>
/// Pure and in memory: pass the result of <see cref="INodeMembership.GetLivenessSnapshotAsync"/>. The snapshot holds
/// each node's current incarnation only, so a restarted node counts under its new incarnation.
/// </remarks>
[PublicAPI]
public static class NodeMetadataAgreement
{
    /// <summary>
    /// Groups the <see cref="NodeLivenessState.Alive"/> and <see cref="NodeLivenessState.Suspected"/> incarnations in
    /// <paramref name="snapshots"/> by their value for <paramref name="key"/>, compared ordinally.
    /// </summary>
    /// <param name="snapshots">The liveness snapshot to compare. Dead incarnations are ignored.</param>
    /// <param name="key">The metadata key to compare.</param>
    /// <returns>
    /// The value groups, largest first, then by ordinal value, with the group of incarnations that lack the key last.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshots"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public static NodeMetadataAgreementResult Compare(IEnumerable<NodeLivenessSnapshot> snapshots, string key)
    {
        Argument.IsNotNull(snapshots);
        Argument.IsNotNullOrWhiteSpace(key);

        var present = new Dictionary<string, List<NodeIdentity>>(StringComparer.Ordinal);
        List<NodeIdentity>? missing = null;

        foreach (var snapshot in snapshots)
        {
            if (snapshot.State is not (NodeLivenessState.Alive or NodeLivenessState.Suspected))
            {
                continue;
            }

            if (snapshot.Metadata.TryGetValue(key, out var value))
            {
                if (!present.TryGetValue(value, out var nodes))
                {
                    present[value] = nodes = [];
                }

                nodes.Add(snapshot.Identity);
            }
            else
            {
                (missing ??= []).Add(snapshot.Identity);
            }
        }

        var groups = present
            .Select(static pair => new NodeMetadataValueGroup(pair.Key, _Order(pair.Value)))
            .OrderByDescending(static group => group.Nodes.Count)
            .ThenBy(static group => group.Value, StringComparer.Ordinal)
            .ToList();

        if (missing is not null)
        {
            groups.Add(new NodeMetadataValueGroup(Value: null, _Order(missing)));
        }

        return new NodeMetadataAgreementResult(key, groups, IsUnanimous: groups is [{ Value: not null }]);
    }

    private static NodeIdentity[] _Order(List<NodeIdentity> nodes)
    {
        return
        [
            .. nodes
                .OrderBy(static identity => identity.NodeId.Value, StringComparer.Ordinal)
                .ThenBy(static identity => identity.Incarnation.Value),
        ];
    }
}

/// <summary>Represents how the live incarnations of a snapshot agree on one metadata key.</summary>
/// <param name="Key">The compared metadata key.</param>
/// <param name="Groups">
/// One group per distinct value, largest first, then by ordinal value, with the missing-key group last. Empty when the
/// snapshot held no alive or suspected incarnation.
/// </param>
/// <param name="IsUnanimous">
/// <see langword="true"/> when every considered incarnation carries the key with one value; <see langword="false"/>
/// when values differ, any incarnation lacks the key, or none was considered.
/// </param>
[PublicAPI]
public sealed record NodeMetadataAgreementResult(
    string Key,
    IReadOnlyList<NodeMetadataValueGroup> Groups,
    bool IsUnanimous
);

/// <summary>Represents the incarnations that share one value for a metadata key.</summary>
/// <param name="Value">The shared value, or <see langword="null"/> for the incarnations that lack the key.</param>
/// <param name="Nodes">The incarnations, ordered by node id then incarnation.</param>
[PublicAPI]
public sealed record NodeMetadataValueGroup(string? Value, IReadOnlyList<NodeIdentity> Nodes);
