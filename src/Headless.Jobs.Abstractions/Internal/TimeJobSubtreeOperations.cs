// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;

namespace Headless.Jobs.Internal;

/// <summary>
/// Shared in-memory operations over a hydrated <see cref="TimeJobEntity"/> subtree, used identically by the EF
/// providers/claim strategies and the in-memory provider so a claim's executable set is shaped the same way regardless
/// of backend.
/// </summary>
internal static class TimeJobSubtreeOperations
{
    /// <summary>
    /// KTD2: keeps only the descendants the claim actually leased. The claimed set is prefix-closed (a node is claimed
    /// only after its parent chain was), so pruning the hydrated tree to it yields exactly the executable subtree — a
    /// node below a non-idle frontier the claim stopped at (terminalized/running) is dropped rather than executed
    /// unclaimed.
    /// </summary>
    internal static void PruneToClaimedSet(TimeJobEntity node, HashSet<Guid> claimedIds)
    {
        var kept = new List<TimeJobEntity>(node.Children.Count);

        foreach (var child in node.Children)
        {
            if (!claimedIds.Contains(child.Id))
            {
                continue;
            }

            PruneToClaimedSet(child, claimedIds);
            kept.Add(child);
        }

        node.Children = kept;
    }
}
