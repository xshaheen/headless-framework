// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Cluster;

/// <summary>
/// The module's live change source: each read takes one liveness snapshot and compares it with the cursor, so a node
/// whose state, role, or metadata changed, a node that appeared, and a node that left the snapshot each go out once.
/// </summary>
/// <remarks>
/// Every node compares the same store read against the same cursor, so a cursor one node returned resumes on any
/// other. A change's version is the read's time in UTC ticks: the shell keeps only a version newer than the last one
/// it sent for an object, and a hash of the node would lose a flip back to an earlier state. Health checks need no
/// read here: their results live in each process and go out when a probe completes.
/// </remarks>
internal sealed class NodeChangeSource(
    string moduleId,
    ClusterServices services,
    TimeProvider timeProvider,
    int pageSize
)
{
    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly ClusterServices _services = Argument.IsNotNull(services);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly int _pageSize = Argument.IsPositive(pageSize);

    public async Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        return NodeChangeCursor.Format(await _ReadVersionsAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads the node changes since <paramref name="cursor"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="cursor"/> is not a node change cursor.</exception>
    public async Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        var known = NodeChangeCursor.Parse(cursor);

        if (!_services.HasCoordination)
        {
            return new DashboardChangeBatch { Changes = [], Cursor = cursor };
        }

        var current = await _ReadVersionsAsync(cancellationToken).ConfigureAwait(false);
        var version = ClusterObjects.Version(_timeProvider.GetUtcNow());
        var changed = current
            .Where(pair =>
                !known.TryGetValue(pair.Key, out var was) || !string.Equals(was, pair.Value, StringComparison.Ordinal)
            )
            .Select(static pair => (Id: pair.Key, Version: (string?)pair.Value));
        var removed = known
            .Keys.Where(id => !current.ContainsKey(id))
            .Select(static id => (Id: id, Version: (string?)null));
        var pending = changed.Concat(removed).OrderBy(static change => change.Id, StringComparer.Ordinal).ToList();
        var page = pending.Take(_pageSize).ToList();

        // The next cursor moves only past the changes this page emitted, so the rest come back on the next read.
        foreach (var (id, nodeVersion) in page)
        {
            if (nodeVersion is null)
            {
                known.Remove(id);
            }
            else
            {
                known[id] = nodeVersion;
            }
        }

        return new DashboardChangeBatch
        {
            Changes =
            [
                .. page.Select(change => new DashboardChange
                {
                    Target = ClusterObjects.Ref(_moduleId, ClusterObjects.NodeKind, change.Id),
                    Version = version,
                    // A node moves in and out of Needs you with its state, so every change refreshes it.
                    AffectsAttention = true,
                }),
            ],
            Cursor = NodeChangeCursor.Format(known),
            HasMore = pending.Count > page.Count,
        };
    }

    private async Task<Dictionary<string, string>> _ReadVersionsAsync(CancellationToken cancellationToken)
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!_services.HasCoordination)
        {
            return versions;
        }

        var snapshot = await _services.Membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);

        foreach (var node in snapshot)
        {
            versions[node.Identity.ToString()] = NodeChangeCursor.Version(node);
        }

        return versions;
    }
}
