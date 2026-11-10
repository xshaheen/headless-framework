// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Leases the non-timed idle subtrees beneath ALREADY-CLAIMED roots, frontier by frontier, down to
/// <c>MaxChainDepth</c>. Shared by the two relational claim paths that must pre-lease a chain — the scheduled tree
/// claim (<c>EfCoreCasJobsClaimStrategy</c>) and the immediate-dispatch acquire
/// (<c>BasePersistenceProvider.AcquireImmediateTimeJobsAsync</c>) — because the executor runs a claimed chain by
/// in-process recursion and fences EVERY node on its own generation before invoking it. A hydrated-but-unleased
/// descendant therefore fails that fence and is stranded <c>Idle</c>, so any path that hydrates a subtree for
/// execution must lease it here first.
/// </summary>
internal static class JobsSubtreeLeaseWalk
{
    /// <summary>
    /// Leases the non-timed idle descendants of every root in <paramref name="roots"/> and returns, PER ROOT, the
    /// generation each claimed node (root included) was granted, so the caller can prune each hydrated tree to the
    /// claimed set and fence every node's writes on its own token — a node below a frontier the walk stopped at is
    /// never leased and must never execute.
    /// </summary>
    /// <remarks>
    /// The walk advances every root one level at a time, so a depth level costs one discovery SELECT for the whole
    /// batch. The grant stays one statement per root: its fence and the deadline it copies are tied to that root's
    /// own row, and roots claimed together carry distinct deadlines. Each descendant draws its own generation, so one
    /// child's token can never fence a sibling, and expires with its root.
    /// </remarks>
    /// <param name="onBeforeFirstLease">
    /// TEST SEAM. Invoked exactly once — after the first frontier's children are discovered but before their grant —
    /// so a test can deterministically invalidate the root's lease and drive the fence. Always
    /// <see langword="null"/> in production.
    /// </param>
    public static async Task<Dictionary<Guid, Dictionary<Guid, long>>> LeaseNonTimedDescendantsAsync<TTimeJob>(
        DbContext context,
        IReadOnlyCollection<JobsGrantedRow> roots,
        string owner,
        TimeSpan leaseDuration,
        int maxChainDepth,
        Func<Task>? onBeforeFirstLease = null,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        var jobs = context.Set<TTimeJob>();
        var table = JobsLeaseRows.TimeJobs<TTimeJob>(context);
        var columns = JobsLeaseGrant.Columns<TTimeJob>(context);
        var claimedByRoot = new Dictionary<Guid, Dictionary<Guid, long>>(roots.Count);
        var rootGenerations = new Dictionary<Guid, long>(roots.Count);

        // Each frontier node carries the root whose lease it inherits, so one discovery SELECT serves the batch.
        var frontier = new List<(Guid NodeId, Guid RootId)>(roots.Count);

        foreach (var root in roots)
        {
            claimedByRoot[root.Id] = new Dictionary<Guid, long> { [root.Id] = root.Generation };
            rootGenerations[root.Id] = root.Generation;
            frontier.Add((root.Id, root.Id));
        }

        var seamFired = false;

        for (var depth = 1; frontier.Count != 0 && depth < maxChainDepth; depth++)
        {
            var rootsByParent = frontier.ToLookup(x => x.NodeId, x => x.RootId);
            var parentIdsByRoot = frontier
                .GroupBy(x => x.RootId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.NodeId).Distinct().ToArray());
            var parentIds = rootsByParent.Select(g => g.Key).ToArray();

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
                foreach (var rootId in rootsByParent[child.ParentId!.Value])
                {
                    if (!childIdsByRoot.TryGetValue(rootId, out var bucket))
                    {
                        bucket = [];
                        childIdsByRoot[rootId] = bucket;
                    }

                    bucket.Add(child.Id);
                }
            }

            if (!seamFired && onBeforeFirstLease is not null)
            {
                seamFired = true;
                await onBeforeFirstLease().ConfigureAwait(false);
            }

            var next = new List<(Guid NodeId, Guid RootId)>(children.Length);

            foreach (var (rootId, childIds) in childIdsByRoot)
            {
                // The statement re-asserts every eligibility arm on the updated row rather than trusting discovery: a
                // child rescheduled, re-parented, or started since must not be claimed as an in-tree continuation. The
                // root fence refuses the whole level once the walk outlived the root's lease or another attempt took
                // the root, so a stale walk never splits a tree between two claims.
                var set = JobsRowSet.For<TTimeJob>(context);
                var status = set.Column(nameof(TimeJobEntity.Status));
                var rootParameter = set.Parameter(nameof(TimeJobEntity.Id), rootId);
                var generationParameter = set.LeaseParameter(
                    nameof(Headless.Fencing.RowLeaseSnapshot.Generation),
                    rootGenerations[rootId]
                );
                var condition =
                    $"{status} = {set.Parameter(nameof(TimeJobEntity.Status), JobStatus.Idle)}"
                    + $" AND {set.Column(nameof(TimeJobEntity.ExecutionTime))} IS NULL"
                    + $" AND {set.InIds(nameof(TimeJobEntity.ParentId), parentIdsByRoot[rootId])}"
                    + $" AND {JobsLeaseGrant.CurrentFence(columns, "jobs_root", rootParameter, generationParameter)}";
                set.DatabaseNow(nameof(TimeJobEntity.UpdatedAt));

                var granted = await JobsLeaseGrant
                    .GrantAsync<TTimeJob>(
                        table,
                        context,
                        set,
                        childIds,
                        condition,
                        JobsLeaseGrant.StoredDeadline(columns, "jobs_deadline", rootParameter),
                        leaseDuration,
                        owner,
                        // A re-acquired root's children may still carry this owner's earlier grant; the root fence is
                        // what keeps a foreign claim out of the tree.
                        reclaimOwn: true,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                foreach (var row in granted)
                {
                    claimedByRoot[rootId][row.Id] = row.Generation;
                    next.Add((row.Id, rootId));
                }
            }

            frontier = next;
        }

        return claimedByRoot;
    }
}
