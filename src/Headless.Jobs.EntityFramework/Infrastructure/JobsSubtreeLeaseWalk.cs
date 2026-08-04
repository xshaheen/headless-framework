// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Microsoft.EntityFrameworkCore;

#pragma warning disable MA0133 // EF must keep DateTime.UtcNow in expression trees so providers translate the database clock before the DateTimeOffset assignment.
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
    /// <para>
    /// A one-root call takes <see cref="_WalkSingleRootAsync"/>, which runs the SAME three statements per level with
    /// none of the per-level attribution bookkeeping (no lookup, no per-root bucketing): every discovered child
    /// provably belongs to the only root. The scheduled tree claim walks one root at a time by construction, so that
    /// is the hot path.
    /// </para>
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
        if (rootIds.Count == 1)
        {
            var soleRootId = rootIds.First();
            var claimedIds = await _WalkSingleRootAsync(
                    jobs,
                    soleRootId,
                    owner,
                    maxChainDepth,
                    onBeforeFirstLease,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return new Dictionary<Guid, HashSet<Guid>>(1) { [soleRootId] = claimedIds };
        }

        return await _WalkRootBatchAsync(jobs, rootIds, owner, maxChainDepth, onBeforeFirstLease, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// One-root walk: the frontier is a plain id array because every child discovered under it inherits the only
    /// root's lease. Statement-for-statement identical to <see cref="_WalkRootBatchAsync"/> — same discovery SELECT,
    /// same fenced lease UPDATE, same frontier re-read — minus the attribution bookkeeping those shared statements
    /// would otherwise be fed from.
    /// </summary>
    private static async Task<HashSet<Guid>> _WalkSingleRootAsync<TTimeJob>(
        DbSet<TTimeJob> jobs,
        Guid rootId,
        string owner,
        int maxChainDepth,
        Func<Task>? onBeforeFirstLease,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        // KTD2: the exact set of claimed ids (root + leased descendants) the caller rebuilds the tree strictly from.
        var claimedIds = new HashSet<Guid> { rootId };
        var frontier = new[] { rootId };

        for (var depth = 1; frontier.Length != 0 && depth < maxChainDepth; depth++)
        {
            var children = await _DiscoverIdleNonTimedChildrenAsync(jobs, frontier, cancellationToken)
                .ConfigureAwait(false);

            if (children.Length == 0)
            {
                break;
            }

            var childIds = Array.ConvertAll(children, x => x.Id);

            // KTD4 test seam: fire once, between discovery and the first lease UPDATE. Null (no-op) in production.
            if (depth == 1 && onBeforeFirstLease is not null)
            {
                await onBeforeFirstLease().ConfigureAwait(false);
            }

            var leased = await _LeaseChildrenAsync(jobs, childIds, frontier, rootId, owner, cancellationToken)
                .ConfigureAwait(false);

            // Children existed but none were leased: either the root lease was lost (fence failed) or every child
            // became ineligible. Stop the walk and treat the claim as bounded to what was already stamped.
            if (leased == 0)
            {
                break;
            }

            // Descend into exactly the children we actually leased (still Idle, now owned by us). A child that raced
            // to a non-idle state is not owned by us here, so the frontier — and the claimed set — stops there.
            frontier = await _ReadStillOwnedAsync(jobs, childIds, owner, cancellationToken).ConfigureAwait(false);
            claimedIds.UnionWith(frontier);
        }

        return claimedIds;
    }

    /// <summary>
    /// Multi-root walk: one discovery SELECT and one frontier re-read serve the WHOLE batch per level, with each
    /// discovered descendant attributed back to the root whose lease it inherits.
    /// </summary>
    private static async Task<Dictionary<Guid, HashSet<Guid>>> _WalkRootBatchAsync<TTimeJob>(
        DbSet<TTimeJob> jobs,
        IReadOnlyCollection<Guid> rootIds,
        string owner,
        int maxChainDepth,
        Func<Task>? onBeforeFirstLease,
        CancellationToken cancellationToken
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

        for (var depth = 1; frontier.Count != 0 && depth < maxChainDepth; depth++)
        {
            // Defensive only: by construction a frontier node maps to exactly one root, because discovery admits
            // Status == Idle rows and every root in the batch is already Queued/InProgress — so a claimed root can
            // never surface as another root's discovered child. The lookup keeps the attribution correct rather than
            // resting on that argument.
            var rootsByParent = frontier.ToLookup(x => x.NodeId, x => x.RootId);
            var parentIdsByRoot = frontier
                .GroupBy(x => x.RootId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.NodeId).Distinct().ToArray());
            var parentIds = rootsByParent.Select(g => g.Key).ToArray();

            var children = await _DiscoverIdleNonTimedChildrenAsync(jobs, parentIds, cancellationToken)
                .ConfigureAwait(false);

            if (children.Length == 0)
            {
                break;
            }

            var childIdsByRoot = new Dictionary<Guid, List<Guid>>();

            foreach (var (childId, childParentId) in children)
            {
                if (childParentId is not { } parentId)
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

                    bucket.Add(childId);
                }
            }

            // KTD4 test seam: fire once, between discovery and the first lease UPDATE, so a test can invalidate a
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

                var leased = await _LeaseChildrenAsync(
                        jobs,
                        childIds,
                        parentIdsByRoot[rootId],
                        rootId,
                        owner,
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
            var stillOurs = await _ReadStillOwnedAsync(jobs, candidateIds, owner, cancellationToken)
                .ConfigureAwait(false);

            var leasedIds = new HashSet<Guid>(stillOurs);
            frontier = [.. leasedCandidates.Where(x => leasedIds.Contains(x.NodeId))];

            foreach (var (nodeId, rootId) in frontier)
            {
                claimedIdsByRoot[rootId].Add(nodeId);
            }
        }

        return claimedIdsByRoot;
    }

    /// <summary>
    /// Discovery SELECT for one depth level, shared by both walks so there is exactly one such statement shape.
    /// It runs FIRST, before any lease UPDATE: the descendant lease stamps LockedUntil by copying the root's deadline
    /// (a subquery, NOT a clock expression) — so it must never go on the wire for a childless/leaf frontier as a
    /// 0-row statement, or the DB-clock conformance assertion (which requires every LockedUntil deadline write to
    /// contain the server clock) would trip on that spurious copy.
    /// </summary>
    private static async Task<(Guid Id, Guid? ParentId)[]> _DiscoverIdleNonTimedChildrenAsync<TTimeJob>(
        DbSet<TTimeJob> jobs,
        Guid[] parentIds,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
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

        return Array.ConvertAll(children, x => (x.Id, x.ParentId));
    }

    /// <summary>
    /// Leases one root's discovered children, COPYING the root's persisted LockedUntil via a database-evaluated
    /// subquery (KTD2 invariant 2) — no clock function — so every level shares the root's exact deadline on both
    /// providers. The predicate is fully reasserted inside the UPDATE, never trusting the discovery snapshot:
    /// <list type="bullet">
    /// <item>
    /// <c>Status == Idle &amp;&amp; ExecutionTime == null</c> + parent-linkage — a child rescheduled (given an
    /// ExecutionTime) or re-parented between the discovery SELECT and this UPDATE must NOT be claimed as an immediate
    /// in-tree continuation, bypassing the timed gate (U5).
    /// </item>
    /// <item>
    /// <c>EXISTS(root still owned by THIS claimant with an UNEXPIRED lease, DB clock)</c> — if the frontier walk
    /// outlived LeaseDuration (short lease / DB stall) another node may have reclaimed the root and stamped these
    /// descendants first; without this fence our stale UPDATE would overwrite their owner and split ownership (winner
    /// runs the root, we own an orphaned tail). Status-agnostic on purpose: the scheduled claim leaves the root Queued
    /// while the immediate acquire leaves it InProgress.
    /// </item>
    /// </list>
    /// </summary>
    private static Task<int> _LeaseChildrenAsync<TTimeJob>(
        DbSet<TTimeJob> jobs,
        Guid[] childIds,
        Guid[] parentIds,
        Guid rootId,
        string owner,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        return jobs.Where(x =>
                ((IEnumerable<Guid>)childIds).Contains(x.Id)
                && x.Status == JobStatus.Idle
                && x.ExecutionTime == null
                && x.ParentId != null
                && ((IEnumerable<Guid>)parentIds).Contains(x.ParentId.Value)
                && jobs.Any(r => r.Id == rootId && r.OwnerId == owner && r.LockedUntil > DateTime.UtcNow)
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, owner)
                        .SetProperty(x => x.LockedUntil, _ => jobs.Where(r => r.Id == rootId).Max(r => r.LockedUntil))
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            );
    }

    /// <summary>
    /// Re-reads which lease candidates are still Idle AND still ours — the nodes the walk may descend into.
    /// </summary>
    private static Task<Guid[]> _ReadStillOwnedAsync<TTimeJob>(
        DbSet<TTimeJob> jobs,
        Guid[] candidateIds,
        string owner,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        return jobs.AsNoTracking()
            .Where(x =>
                ((IEnumerable<Guid>)candidateIds).Contains(x.Id) && x.Status == JobStatus.Idle && x.OwnerId == owner
            )
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
    }
}
