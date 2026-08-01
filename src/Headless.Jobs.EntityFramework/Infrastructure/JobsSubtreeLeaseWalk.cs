// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// R12/KTD2: leases the non-timed idle subtrees beneath ALREADY-CLAIMED roots, frontier by frontier, down to
/// <c>MaxChainDepth</c>. Shared by the two relational claim paths that must pre-lease a chain — the scheduled tree
/// claim (<c>EfCoreCasJobsClaimStrategy</c>) and the immediate-dispatch acquire
/// (<c>BasePersistenceProvider.AcquireImmediateTimeJobsAsync</c>) — because the executor runs a claimed chain by
/// in-process recursion and fences EVERY node on lease renewal before invoking it. A hydrated-but-unleased
/// descendant therefore fails that fence and is stranded <c>Idle</c> forever, so any path that hydrates a subtree
/// for execution must lease it here first.
/// </summary>
internal static class JobsSubtreeLeaseWalk
{
    /// <summary>
    /// Leases the non-timed idle descendants of every root in <paramref name="rootIds"/> and returns, PER ROOT, the
    /// exact set of claimed ids (root + leased descendants) so the caller can prune each hydrated tree to it — a node
    /// below a frontier the walk stopped at is never leased and must never execute.
    /// </summary>
    /// <remarks>
    /// The walk advances EVERY root one level at a time (mirroring <see cref="MappingExtensions.AttachNonTimedDescendantsAsync"/>),
    /// so a depth level costs one discovery SELECT plus one frontier re-read for the WHOLE batch instead of a pair per
    /// root — a batch of childless roots (the common case) collapses from one SELECT per root to a single SELECT. The
    /// lease UPDATE stays one statement per root on purpose: both its ownership fence and the deadline it copies are
    /// correlated to that root's own row, and roots claimed in the same batch carry distinct persisted deadlines, so
    /// there is no single-statement form that keeps each descendant tied to ITS root.
    /// </remarks>
    /// <param name="onBeforeFirstLease">
    /// TEST SEAM (KTD4). Invoked exactly once — after the first frontier's children are discovered but before their
    /// lease UPDATE — so a test can deterministically invalidate the root's lease (expire it, or reassign its owner)
    /// and drive the <c>EXISTS(root still owned by me, lease unexpired)</c> fence without racing statement latency
    /// against a wall-clock deadline. Always <see langword="null"/> in production; production callers omit it.
    /// </param>
    public static async Task<Dictionary<Guid, HashSet<Guid>>> LeaseNonTimedDescendantsAsync<TTimeJob>(
        DbSet<TTimeJob> jobs,
        IReadOnlyCollection<Guid> rootIds,
        string owner,
        int maxChainDepth,
        Func<Task>? onBeforeFirstLease = null,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        // KTD2: accumulate, per root, the exact set of claimed ids (root + leased descendants) so the caller rebuilds
        // each tree strictly from it — a node below a non-idle frontier is never leased and must never execute
        // unclaimed.
        var claimedIdsByRoot = new Dictionary<Guid, HashSet<Guid>>(rootIds.Count);

        // The frontier carries its owning root alongside each node, which is what lets one batched SELECT serve every
        // root and still attribute each discovered descendant back to the root whose lease it inherits.
        var frontier = new List<(Guid NodeId, Guid RootId)>(rootIds.Count);

        foreach (var rootId in rootIds)
        {
            claimedIdsByRoot[rootId] = [rootId];
            frontier.Add((rootId, rootId));
        }

        var depth = 1;

        while (frontier.Count != 0 && depth < maxChainDepth)
        {
            // A frontier node maps to more than one root only when the batch contains both a node and one of its own
            // non-timed ancestors; the lookup keeps that degenerate case attributed to every root that reaches it,
            // exactly as the former per-root walks did.
            var rootsByParent = frontier.ToLookup(x => x.NodeId, x => x.RootId);
            var parentIdsByRoot = frontier
                .GroupBy(x => x.RootId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.NodeId).Distinct().ToArray());
            var parentIds = rootsByParent.Select(g => g.Key).ToArray();

            // Discover the idle non-timed children of the current frontier FIRST. The descendant lease UPDATE stamps
            // LockedUntil by copying the root's deadline (a subquery, NOT a clock expression) — so it must never go on
            // the wire for a childless/leaf frontier as a 0-row statement, or the DB-clock conformance assertion (which
            // requires every LockedUntil deadline write to contain the server clock) would trip on that spurious copy.
            var children = await jobs.AsNoTracking()
                .Where(x =>
                    x.ParentId != null
                    && ((IEnumerable<Guid>)parentIds).Contains(x.ParentId.Value)
                    && x.Status == JobStatus.Idle
                    && x.ExecutionTime == null
                )
                .Select(x => new { x.Id, x.ParentId })
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (children.Length == 0)
            {
                break;
            }

            var childIdsByRoot = new Dictionary<Guid, List<Guid>>();

            foreach (var child in children)
            {
                if (child.ParentId is not { } parentId)
                {
                    continue;
                }

                foreach (var rootId in rootsByParent[parentId])
                {
                    if (!childIdsByRoot.TryGetValue(rootId, out var bucket))
                    {
                        bucket = [];
                        childIdsByRoot[rootId] = bucket;
                    }

                    bucket.Add(child.Id);
                }
            }

            // KTD4 test seam: fire once, between discovery and the first lease UPDATE, so a test can invalidate the
            // root lease and exercise the EXISTS fence below deterministically. Null (and thus a no-op) in production.
            if (depth == 1 && onBeforeFirstLease is not null)
            {
                await onBeforeFirstLease().ConfigureAwait(false);
            }

            // Candidate next frontier: only the roots that actually leased at least one child stay in the walk.
            var leasedCandidates = new List<(Guid NodeId, Guid RootId)>(children.Length);

            foreach (var (rootId, childIdBucket) in childIdsByRoot)
            {
                var childIds = childIdBucket.ToArray();
                var parentIdsForRoot = parentIdsByRoot[rootId];

                // Lease them, COPYING the root's persisted LockedUntil via a database-evaluated subquery (KTD2 invariant 2)
                // — no clock function — so every level shares the root's exact deadline on both providers. The predicate is
                // fully reasserted inside the UPDATE, never trusting the discovery snapshot:
                //   * Status == Idle && ExecutionTime == null && parent-linkage — a child rescheduled (given an
                //     ExecutionTime) or re-parented between the discovery SELECT and this UPDATE must NOT be claimed as an
                //     immediate in-tree continuation, bypassing the timed gate (U5).
                //   * EXISTS(root still owned by THIS claimant with an UNEXPIRED lease, DB clock) — if the frontier walk
                //     outlived LeaseDuration (short lease / DB stall) another node may have reclaimed the root and stamped
                //     these descendants first; without this fence our stale UPDATE would overwrite their owner and split
                //     ownership (winner runs the root, we own an orphaned tail). Status-agnostic on purpose: the scheduled
                //     claim leaves the root Queued while the immediate acquire leaves it InProgress.
                var leased = await jobs.Where(x =>
                        ((IEnumerable<Guid>)childIds).Contains(x.Id)
                        && x.Status == JobStatus.Idle
                        && x.ExecutionTime == null
                        && x.ParentId != null
                        && ((IEnumerable<Guid>)parentIdsForRoot).Contains(x.ParentId.Value)
                        && jobs.Any(r => r.Id == rootId && r.OwnerId == owner && r.LockedUntil > DateTime.UtcNow)
                    )
                    .ExecuteUpdateAsync(
                        setter =>
                            setter
                                .SetProperty(x => x.OwnerId, owner)
                                .SetProperty(
                                    x => x.LockedUntil,
                                    _ => jobs.Where(r => r.Id == rootId).Max(r => r.LockedUntil)
                                )
                                .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                // Children existed but none were leased: either the root lease was lost (fence failed) or every child
                // became ineligible. Stop THIS root's walk and treat its claim as bounded to what was already stamped —
                // the caller prunes the hydrated tree to that claimed set, and the unexecuted claimed root is recovered
                // by the stalled-lease sweep, which re-claims descendants fresh.
                if (leased == 0)
                {
                    continue;
                }

                foreach (var childId in childIds)
                {
                    leasedCandidates.Add((childId, rootId));
                }
            }

            if (leasedCandidates.Count == 0)
            {
                break;
            }

            // Descend into exactly the children we actually leased (still Idle, now owned by us). A child that raced to
            // a non-idle state is not owned by us here, so the frontier — and the claimed set — stops there. One re-read
            // covers every root; the candidate pairs carry the attribution back.
            var candidateIds = leasedCandidates.Select(x => x.NodeId).Distinct().ToArray();
            var stillOurs = await jobs.AsNoTracking()
                .Where(x =>
                    ((IEnumerable<Guid>)candidateIds).Contains(x.Id) && x.Status == JobStatus.Idle && x.OwnerId == owner
                )
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            var leasedIds = new HashSet<Guid>(stillOurs);
            frontier = leasedCandidates.Where(x => leasedIds.Contains(x.NodeId)).ToList();

            foreach (var (nodeId, rootId) in frontier)
            {
                claimedIdsByRoot[rootId].Add(nodeId);
            }

            depth++;
        }

        return claimedIdsByRoot;
    }
}
