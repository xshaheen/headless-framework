// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;

namespace Tests;

/// <summary>
/// One store as two nodes see it. <see cref="DeadNode" /> claims the chain and then stops; <see cref="LiveNode" />
/// reclaims and runs what is left. A single-process store passes itself as both.
/// </summary>
public interface IChainReclaimScenarioBackend
{
    /// <summary>The node that claims the chain root and dies mid-chain.</summary>
    IJobPersistenceProvider<TimeJobEntity, CronJobEntity> DeadNode { get; }

    /// <summary>The node that survives, reclaims, and claims the stranded descendant.</summary>
    IJobPersistenceProvider<TimeJobEntity, CronJobEntity> LiveNode { get; }

    /// <summary>Makes each row's lease lapse on the store's own clock, as a node that stopped renewing leaves it.</summary>
    Task LapseLeasesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// A chain runs on the node that claimed its root, so its untimed descendants hold no claim of their own. When that
/// node dies after the root completed, the reclaim must leave each such descendant runnable on another node, still
/// behind its parent, or settle it. Runs on memory, PostgreSQL, and SQL Server.
/// </summary>
public static class JobsChainReclaimScenarios
{
    private const int _PeekAttempts = 3;

    /// <summary>
    /// The node dies while the root's on-success child runs. The dead-node reclaim leaves the child to another node,
    /// keeps its own child behind it until it succeeds, and skips the root's on-failure child.
    /// </summary>
    public static async Task DeadNodeChildRunsOnAnotherNodeAsync(
        IChainReclaimScenarioBackend backend,
        CancellationToken cancellationToken
    )
    {
        // given: root -> Then(child) -> Then(grandchild), and root -> Catch(refund); the root succeeded and the
        // child is running when the node dies
        var chain = await _StartChildAfterRootSucceedsAsync(backend, cancellationToken);
        await backend.LapseLeasesAsync([chain.Child, chain.Grandchild, chain.Refund], cancellationToken);

        // when: the cluster declares the node dead and reclaims what it held, then reconciles the chain
        var reclaimed = await backend.LiveNode.ReleaseDeadNodeTimeJobResourcesAsync(chain.Owner, cancellationToken);
        await backend.LiveNode.ApplyParentTerminalRunConditionsAsync(parentId: null, cancellationToken);

        // then: the running child and the two waiting descendants go back, the child with a due time of its own
        reclaimed.Released.Should().Be(3);
        var child = await _ReadAsync(backend, chain.Child, cancellationToken);
        child.Status.Should().Be(JobStatus.Idle);
        child.ExecutionTime.Should().NotBeNull("a reclaimed descendant needs a due time for a claim to select it");

        // and the root's on-failure child is skipped, because the root succeeded
        var refund = await _ReadAsync(backend, chain.Refund, cancellationToken);
        refund.Status.Should().Be(JobStatus.Skipped);

        // and the grandchild cannot run before its parent does
        (await backend.LiveNode.AcquireImmediateTimeJobsAsync([chain.Grandchild], cancellationToken))
            .Should()
            .BeEmpty("a descendant never runs before its parent succeeds");

        // and another node claims the child through the scheduler's timed claim, and runs it
        var claimedChild = await _ClaimThroughPeekAsync(backend.LiveNode, chain.Root, chain.Child, cancellationToken);
        claimedChild.Children.Should().BeEmpty("the grandchild is no longer run in-tree under the child's claim");
        await _SettleAsync(backend.LiveNode, claimedChild, JobStatus.Succeeded, cancellationToken);

        // and the grandchild follows once the child succeeded
        var claimedGrandchild = await _ClaimThroughPeekAsync(
            backend.LiveNode,
            chain.Child,
            chain.Grandchild,
            cancellationToken
        );
        await _SettleAsync(backend.LiveNode, claimedGrandchild, JobStatus.Succeeded, cancellationToken);
        (await _ReadAsync(backend, chain.Grandchild, cancellationToken)).Status.Should().Be(JobStatus.Succeeded);
    }

    /// <summary>
    /// The node stops renewing while the root's on-success child runs and is never declared dead. The stalled-lease
    /// reclaim leaves the child to another node.
    /// </summary>
    public static async Task StalledChildRunsOnAnotherNodeAsync(
        IChainReclaimScenarioBackend backend,
        CancellationToken cancellationToken
    )
    {
        // given: the root succeeded and its on-success child is running when its lease lapses
        var chain = await _StartChildAfterRootSucceedsAsync(backend, cancellationToken);
        await backend.LapseLeasesAsync([chain.Child], cancellationToken);

        // when: the stalled-lease sweep reclaims it
        var reclaimed = await backend.LiveNode.ReclaimStalledTimeJobsAsync(cancellationToken);
        await backend.LiveNode.ApplyParentTerminalRunConditionsAsync(parentId: null, cancellationToken);

        // then: the child goes back with a due time and another node runs it
        reclaimed.Released.Should().Be(1);
        var child = await _ReadAsync(backend, chain.Child, cancellationToken);
        child.Status.Should().Be(JobStatus.Idle);
        child.ExecutionTime.Should().NotBeNull("a reclaimed descendant needs a due time for a claim to select it");

        var claimedChild = await _ClaimThroughPeekAsync(backend.LiveNode, chain.Root, chain.Child, cancellationToken);
        await _SettleAsync(backend.LiveNode, claimedChild, JobStatus.Succeeded, cancellationToken);
        (await _ReadAsync(backend, chain.Child, cancellationToken)).Status.Should().Be(JobStatus.Succeeded);
    }

    private static async Task<_Chain> _StartChildAfterRootSucceedsAsync(
        IChainReclaimScenarioBackend backend,
        CancellationToken cancellationToken
    )
    {
        var grandchild = _NewJob("chain-reclaim-grandchild", RunCondition.OnSuccess);
        var child = _NewJob("chain-reclaim-child", RunCondition.OnSuccess, grandchild);
        var refund = _NewJob("chain-reclaim-refund", RunCondition.OnFailure);
        var root = _NewJob("chain-reclaim-root", runCondition: null, child, refund);
        await backend.DeadNode.AddTimeJobsAsync([root], cancellationToken);

        // The root has no execution time, so the immediate acquire takes it with its untimed subtree, as a run-now
        // chain is dispatched.
        var acquired = await backend.DeadNode.AcquireImmediateTimeJobsAsync([root.Id], cancellationToken);
        var claimedRoot = acquired.Should().ContainSingle(x => x.Id == root.Id).Subject;
        var owner = claimedRoot.Lease.Holder!;

        var childRow = await _ReadAsync(backend, child.Id, cancellationToken);
        childRow.Lease.Holder.Should().Be(owner, "the child runs under the root's claim");
        childRow.ExecutionTime.Should().BeNull();

        await _WriteAsync(backend.DeadNode, root.Id, claimedRoot.Lease, JobStatus.Succeeded, cancellationToken);
        await _WriteAsync(backend.DeadNode, child.Id, childRow.Lease, JobStatus.InProgress, cancellationToken);

        return new _Chain(root.Id, child.Id, grandchild.Id, refund.Id, owner);
    }

    // The scheduler peeks the due rows and claims them; the reconcile it runs after a reclaim and after a parent
    // settles makes a past-due descendant due again, so each attempt repeats it. A peek that keeps missing a row it was
    // just handed is a defect, so the attempts are counted, not timed.
    private static async Task<TimeJobEntity> _ClaimThroughPeekAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid parentId,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; attempt <= _PeekAttempts; attempt++)
        {
            await store.ApplyParentTerminalRunConditionsAsync(parentId, cancellationToken);
            var candidates = (await store.GetEarliestTimeJobsAsync(cancellationToken)).Jobs;

            if (!Array.Exists(candidates, x => x.Id == id))
            {
                continue;
            }

            var claimed = await store.QueueTimeJobsAsync(candidates, cancellationToken).ToArrayAsync(cancellationToken);
            var match = Array.Find(claimed, x => x.Id == id);

            if (match is not null)
            {
                return match;
            }
        }

        var row = await store.GetTimeJobByIdAsync(id, cancellationToken);

        throw new InvalidOperationException(
            $"Job {id} was not claimed after {_PeekAttempts} peeks: status {row?.Status}, execution time "
                + $"{row?.ExecutionTime:O}, lease {row?.Lease.State} held by {row?.Lease.Holder}."
        );
    }

    private static async Task _SettleAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        TimeJobEntity claimed,
        JobStatus status,
        CancellationToken cancellationToken
    )
    {
        await _WriteAsync(store, claimed.Id, claimed.Lease, JobStatus.InProgress, cancellationToken);
        await _WriteAsync(store, claimed.Id, claimed.Lease, status, cancellationToken);
    }

    // The fenced write the executor issues to start or settle a row it holds.
    private static async Task _WriteAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid id,
        RowLeaseSnapshot claim,
        JobStatus status,
        CancellationToken cancellationToken
    )
    {
        var write = new JobExecutionState
        {
            FunctionName = "chain",
            JobId = id,
            Type = JobType.TimeJob,
            ClaimOwner = claim.Holder!,
            ClaimGeneration = claim.Generation,
        }.SetProperty(x => x.Status, status);

        (await store.UpdateTimeJobAsync(write, cancellationToken))
            .Should()
            .Be(1, $"the claim's {status} write must land on job {id}");
    }

    private static async Task<TimeJobEntity> _ReadAsync(
        IChainReclaimScenarioBackend backend,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var row = await backend.LiveNode.GetTimeJobByIdAsync(id, cancellationToken);

        return row ?? throw new InvalidOperationException($"Job {id} is missing.");
    }

    // The store stamps each child's parent from the nested graph, as the manager's chain add does.
    private static TimeJobEntity _NewJob(string function, RunCondition? runCondition, params TimeJobEntity[] children)
    {
        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = function,
            RunCondition = runCondition,
            Retries = 1,
            RetryIntervals = [0],
            OnNodeDeath = NodeDeathPolicy.Retry,
            Children = [.. children],
        };
    }

    private sealed record _Chain(Guid Root, Guid Child, Guid Grandchild, Guid Refund, string Owner);
}
