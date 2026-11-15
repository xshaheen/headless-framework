// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Fencing;
using Headless.Sql;
using Headless.Sql.PostgreSql;
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
                        // A root handed back unstarted, or for a retry, leaves its children under the earlier claim's
                        // lease. That lease expires before this claim's, which came later, so it is taken over with the
                        // root. A stale walk racing a newer claim of the same tree meets a child lease that outlives
                        // the root deadline its own snapshot reads, and refuses it.
                        supersededBefore: JobsLeaseGrant.StoredDeadline(columns, "jobs_superseded", rootParameter),
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

    /// <summary>
    /// Slides the lease of the non-timed idle descendants <paramref name="owner"/> still holds beneath
    /// <paramref name="node"/>, frontier by frontier down to <c>MaxChainDepth</c>, to <paramref name="node"/>'s own
    /// stored deadline. Called after a renewal of <paramref name="node"/> landed.
    /// </summary>
    /// <remarks>
    /// A chain's continuations wait <c>Idle</c> under the claim, with the deadline of the node that runs before them,
    /// until that node ends. Renewal slides only the running row, so without this a node that runs longer than one
    /// lease leaves its descendants' leases expired, and their start or skip is refused once it ends. Each statement is
    /// fenced on <paramref name="node"/>'s generation still being current, so a renewal that lost the race to a reclaim
    /// slides nothing, and only a live lease of the same holder slides, so a descendant another claim took is left
    /// alone. A timed descendant is claimed on its own and is never slid.
    /// </remarks>
    public static async Task SlideClaimedDescendantsAsync<TTimeJob>(
        DbContext context,
        FencedRow<Guid> node,
        string owner,
        int maxChainDepth,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        var table = JobsLeaseRows.TimeJobs<TTimeJob>(context);
        var columns = JobsLeaseGrant.Columns<TTimeJob>(context);
        var dialect = table.Dialect;
        var connection = JobsLeaseRows.Connection(context);
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            IReadOnlyCollection<Guid> frontier = [node.Key];

            for (var depth = 1; frontier.Count != 0 && depth < maxChainDepth; depth++)
            {
                var set = JobsRowSet.For<TTimeJob>(context);
                var nodeParameter = set.Parameter(nameof(TimeJobEntity.Id), node.Key);
                var generationParameter = set.LeaseParameter(nameof(RowLeaseSnapshot.Generation), node.Generation);
                var holderParameter = set.LeaseParameter(nameof(RowLeaseSnapshot.Holder), owner);
                var where =
                    $"{set.InIds(nameof(TimeJobEntity.ParentId), frontier)}"
                    + $" AND {set.Column(nameof(TimeJobEntity.Status))} = {set.Parameter(nameof(TimeJobEntity.Status), JobStatus.Idle)}"
                    + $" AND {set.Column(nameof(TimeJobEntity.ExecutionTime))} IS NULL"
                    + $" AND {JobsLeaseGrant.HeldLive(columns, holderParameter)}"
                    + $" AND {JobsLeaseGrant.CurrentFence(columns, "jobs_node", nodeParameter, generationParameter)}";
                var deadline = JobsLeaseGrant.StoredDeadline(columns, "jobs_deadline", nodeParameter);
                var sql =
                    dialect is PostgreSqlDialect || JobsSqlDialects.IsSqlite(dialect)
                        ? $"UPDATE {columns.Table} SET {columns.ExpiresAt} = {deadline} WHERE {where} RETURNING {columns.Id};"
                        : $"UPDATE {columns.Table} SET {columns.ExpiresAt} = {deadline} OUTPUT inserted.{columns.Id} WHERE {where};";

                await using var command = connection.CreateCommand();
                command.Transaction = JobsLeaseRows.Transaction(context);
#pragma warning disable CA2100 // SQL identifiers are provider-delimited EF metadata; runtime values are parameters.
                command.CommandText = dialect.Render(new SqlClockedStatement(sql));
#pragma warning restore CA2100
                // Only the bindings: the slide writes the expiry alone, so it does not stamp the change marker.
                set.ToWrite().Bind?.Invoke(command);

                var slid = new List<Guid>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    slid.Add(await reader.GetFieldValueAsync<Guid>(0, cancellationToken).ConfigureAwait(false));
                }

                frontier = slid;
            }
        }
        finally
        {
            if (openedHere)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }
}
