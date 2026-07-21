// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-neutral conformance for typed <see cref="JobChain"/> runtime semantics on every relational backend
/// (Postgres, SQL Server). Proves the storage-visible behaviors the native SQL introduced in U4 (deep claim/hydration
/// via recursive CTEs) and U5 (timed-descendant claim gate + the set-based release/skip reconcile), plus the U2/KTD6
/// atomic-persistence contract for the whole tree.
/// <para>
/// These scenarios drive the <b>public</b> provider surface (<see cref="IJobPersistenceProvider{TTimeJob,TCronJob}" />
/// claim/reconcile members and <see cref="IJobScheduler.EnqueueAsync(JobChain,System.Threading.CancellationToken)" />)
/// and assert the resulting durable row transitions — the same "storage-visible transitions are the contract" style as
/// <see cref="JobsCoordinationConformanceTests{TFixture}" /> and <see cref="JobsClaimConformanceTests{TFixture}" />. The
/// in-process executor recursion and the non-timed run/skip cascade are provider-agnostic C# (the executor is internal)
/// and are proven in-memory by the U3 unit suite; U7's job is the provider SQL those decisions rest on. A parent's
/// terminal state is simulated with a fenced <c>UpdateTimeJobAsync</c> completion (exactly what the executor issues),
/// then the real provider reconcile is invoked and the durable outcome asserted.
/// </para>
/// Each leaf derives a sealed class with <c>[Collection&lt;TFixture&gt;]</c> and re-declares the methods with
/// <c>[Fact]</c> so the runner discovers them per provider.
/// </summary>
public abstract class JobsChainConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _RunConditionMismatchReason = "Rule RunCondition did not match!";

    // AE1/AE2 (persistence half). A conditional tree flattens onto ParentId/RunCondition rows: Then -> OnSuccess,
    // Catch -> OnFailure, every node persisted atomically, and per-node validation reaches beyond the root.
    public virtual async Task enqueue_persists_conditional_tree_edges()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-persist");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();

            // charge.Then(receipt); charge.Catch(refund); refund.Then(notify) — both edge kinds, depth 3 on the catch arm.
            var builder = JobChain.Start(_Payload("charge"), executionTime: DateTime.UtcNow.AddHours(1));
            builder.Root.Then(_Payload("receipt"));
            var refund = builder.Root.Catch(_Payload("refund"));
            refund.Then(_Payload("notify"));

            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);

            var root = await _ReadNodeAsync(rootId, ct);
            root.Status.Should().Be(JobStatus.Idle);
            root.ParentId.Should().BeNull();
            root.RunCondition.Should().BeNull("the root carries no run condition");

            var rootChildren = await _ChildrenAsync(rootId, ct);
            rootChildren.Should().HaveCount(2);
            var receiptId = rootChildren.Single(c => c.Condition == RunCondition.OnSuccess).Id;
            var refundId = rootChildren.Single(c => c.Condition == RunCondition.OnFailure).Id;

            (await _ReadNodeAsync(receiptId, ct)).RunCondition.Should().Be(RunCondition.OnSuccess);
            var refundRow = await _ReadNodeAsync(refundId, ct);
            refundRow.RunCondition.Should().Be(RunCondition.OnFailure);
            refundRow.ParentId.Should().Be(rootId);

            var refundChildren = await _ChildrenAsync(refundId, ct);
            refundChildren.Should().ContainSingle();
            refundChildren[0].Condition.Should().Be(RunCondition.OnSuccess);
            (await _ReadNodeAsync(refundChildren[0].Id, ct)).Status.Should().Be(JobStatus.Idle);

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(4);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE7 (provider half). A five-node linear chain plus a failure branch is claimed in one root claim: the recursive
    // CTE stamps EVERY descendant beyond the grandchild level with the root's owner + lease, and hydration rebuilds the
    // whole non-timed subtree to the configured depth. A two-level cap would leave the fourth/fifth nodes unstamped.
    public virtual async Task deep_chain_claim_stamps_every_descendant_to_configured_depth()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-deep");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var builder = JobChain.Start(_Payload("n1"), executionTime: DateTime.UtcNow.AddSeconds(1));
            var n2 = builder.Root.Then(_Payload("n2"));
            var n3 = n2.Then(_Payload("n3"));
            var n4 = n3.Then(_Payload("n4"));
            n4.Then(_Payload("n5"));
            n3.Catch(_Payload("n3-catch")); // an off-branch at depth 4 proves both edge kinds hydrate/stamp.

            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);

            // Hydration rebuilds the full non-timed subtree to MaxChainDepth (n1..n5 + n3-catch = 6 nodes).
            var candidates = await _PollEarliestUntilPresentAsync(persistence, rootId, ct);
            var hydratedRoot = candidates.Single(x => x.Id == rootId);
            var allIds = _FlattenIds(hydratedRoot).ToArray();
            allIds.Should().HaveCount(6, "hydration must rebuild every non-timed descendant to the configured depth");

            // The recursive CTE claim stamps every one of them under a single owner + lease.
            var claimed = await persistence.QueueTimeJobsAsync(candidates, ct).ToArrayAsync(ct);
            claimed.Should().Contain(x => x.Id == rootId);

            var rootRow = await _ReadNodeAsync(rootId, ct);
            rootRow.OwnerId.Should().NotBeNullOrWhiteSpace();
            rootRow.LockedUntil.Should().NotBeNull();

            foreach (var id in allIds)
            {
                var row = await _ReadNodeAsync(id, ct);
                row.OwnerId.Should().Be(rootRow.OwnerId, "descendant {0} must be claimed with the root", id);
                row.LockedUntil.Should().Be(rootRow.LockedUntil, "descendant {0} must share the root's lease", id);
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE6. A chain enqueue that throws after buffering the whole tree rolls back atomically — no partial chain survives.
    public virtual async Task chain_enqueue_rolls_back_atomically_leaving_no_rows()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildCoordinatedEnqueueHost("chain-rollback");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var builder = JobChain.Start(_Payload("root"), executionTime: DateTime.UtcNow.AddHours(1));
            var child = builder.Root.Then(_Payload("child"));
            child.Then(_Payload("grandchild"));
            builder.Root.Catch(_Payload("catch"));
            var chain = builder.Build();
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, _, innerCt) =>
                    {
                        (await scheduler.EnqueueAsync(chain, innerCt)).Should().NotBeEmpty();

                        // Abandon the scope after the whole tree is buffered: the transaction never commits.
                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE8 (gate). A due timed descendant is NOT claimable while its parent is still non-terminal — neither the main
    // peek nor the timed-out fallback may surface it. This is the behavior #311 inverts: pre-#311 it fired at its time
    // unconditionally.
    public virtual async Task timed_child_is_not_claimable_while_parent_is_non_terminal()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-gate");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var builder = JobChain.Start(_Payload("root"), executionTime: DateTime.UtcNow.AddSeconds(1));
            builder.Root.Then(_Payload("timed"), executionTime: DateTime.UtcNow.AddMinutes(-2)); // due (past) timed child
            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);
            var timedId = (await _ChildrenAsync(rootId, ct)).Single().Id;

            // Claim the root -> it is now Queued (non-terminal). The timed child is a boundary: never claimed with it.
            await _ClaimRootAsync(persistence, rootId, ct);
            (await _ReadNodeAsync(rootId, ct)).Status.Should().Be(JobStatus.Queued);

            var earliest = await persistence.GetEarliestTimeJobsAsync(ct);
            earliest
                .Should()
                .NotContain(x => x.Id == timedId, "the parent gate keeps a due timed child out of the peek");

            var timedOut = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            timedOut.Should().NotContain(x => x.Id == timedId, "the fallback claim mirrors the same parent gate");

            (await _ReadNodeAsync(timedId, ct)).Status.Should().Be(JobStatus.Idle, "the gated child was never claimed");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE1 (realized) + AE8 (release). On parent success the matching timed OnSuccess child is released (a past-due one
    // re-stamped to now so the staleness-filtered peek claims it promptly), while the non-matching timed OnFailure
    // (Catch) sibling is skipped — the storage-visible form of "root succeeds -> a eligible, b skipped".
    public virtual async Task parent_success_releases_timed_success_child_and_skips_timed_catch_child()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-success");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var pastDue = DateTime.UtcNow.AddMinutes(-2);
            var builder = JobChain.Start(_Payload("root"), executionTime: DateTime.UtcNow.AddSeconds(1));
            builder.Root.Then(_Payload("on-success"), executionTime: pastDue);
            builder.Root.Catch(_Payload("on-failure"), executionTime: pastDue);
            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);
            var children = await _ChildrenAsync(rootId, ct);
            var successId = children.Single(c => c.Condition == RunCondition.OnSuccess).Id;
            var failureId = children.Single(c => c.Condition == RunCondition.OnFailure).Id;

            await _ClaimRootAsync(persistence, rootId, ct);
            await _MarkTerminalAsync(persistence, rootId, JobStatus.Succeeded, ct);

            var beforeReconcile = DateTime.UtcNow;
            await persistence.ApplyParentTerminalRunConditionsAsync(rootId, ct);

            // Matching (OnSuccess) past-due child: released back to Idle, unowned, re-stamped to ~now, and claimable.
            var successRow = await _ReadNodeAsync(successId, ct);
            successRow.Status.Should().Be(JobStatus.Idle);
            successRow.OwnerId.Should().BeNull();
            successRow.SkippedReason.Should().BeNull();
            successRow.ExecutionTime.Should().NotBeNull();
            successRow
                .ExecutionTime!.Value.Should()
                .BeAfter(pastDue.AddMinutes(1), "the past-due matching child is re-stamped forward, not left stale");
            successRow.ExecutionTime.Value.Should().BeCloseTo(beforeReconcile, TimeSpan.FromSeconds(30));

            (await persistence.GetEarliestTimeJobsAsync(ct))
                .Should()
                .Contain(x => x.Id == successId, "the released child is now claimable under the open parent gate");

            // Non-matching (OnFailure) child: skipped with the run-condition-mismatch reason.
            var failureRow = await _ReadNodeAsync(failureId, ct);
            failureRow.Status.Should().Be(JobStatus.Skipped);
            failureRow.SkippedReason.Should().Be(_RunConditionMismatchReason);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE8 (future). A matching timed child scheduled in the future is released on parent success but NOT re-stamped —
    // it keeps its own execution time (stays Idle until then) and only becomes claimable once that time arrives.
    public virtual async Task future_timed_success_child_waits_for_its_own_time_then_becomes_claimable()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-future");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var future = DateTime.UtcNow.AddSeconds(3);
            var builder = JobChain.Start(_Payload("root"), executionTime: DateTime.UtcNow.AddSeconds(1));
            builder.Root.Then(_Payload("future-child"), executionTime: future);
            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);
            var childId = (await _ChildrenAsync(rootId, ct)).Single().Id;

            await _ClaimRootAsync(persistence, rootId, ct);
            await _MarkTerminalAsync(persistence, rootId, JobStatus.Succeeded, ct);
            await persistence.ApplyParentTerminalRunConditionsAsync(rootId, ct);

            var row = await _ReadNodeAsync(childId, ct);
            row.Status.Should().Be(JobStatus.Idle);
            row.SkippedReason.Should().BeNull();
            row.ExecutionTime.Should().NotBeNull();
            row.ExecutionTime!.Value.Should()
                .BeCloseTo(
                    future,
                    TimeSpan.FromMilliseconds(50),
                    "a future matching child keeps its own time, not re-stamped to now"
                );

            // While still future, the fallback must not fire it early.
            (await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct))
                .Should()
                .NotContain(x => x.Id == childId);

            // Once its own time passes, the open parent gate lets it be claimed (bounded poll, no fixed sleep).
            var claimed = await _PollTimedOutUntilClaimedAsync(persistence, childId, ct);
            claimed.Should().NotBeNull("the future child must run once its own execution time arrives");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE9 + AE2 (realized) + the poll-time safety net. On parent FAILURE the timed OnSuccess child (and its whole
    // subtree) is skipped, while the matching timed OnFailure (Catch) child is released. The skip-only safety net skips
    // the non-matching subtree without ever releasing; the per-parent reconcile then releases the matching catch child.
    public virtual async Task parent_failure_skips_timed_success_subtree_and_releases_timed_catch_child()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-failure");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var pastDue = DateTime.UtcNow.AddMinutes(-2);
            var builder = JobChain.Start(_Payload("root"), executionTime: DateTime.UtcNow.AddSeconds(1));
            var onSuccess = builder.Root.Then(_Payload("on-success"), executionTime: pastDue);
            onSuccess.Then(_Payload("grandchild")); // non-timed descendant of the timed child, proves subtree cascade
            builder.Root.Catch(_Payload("on-failure"), executionTime: pastDue);
            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);

            var rootChildren = await _ChildrenAsync(rootId, ct);
            var successId = rootChildren.Single(c => c.Condition == RunCondition.OnSuccess).Id;
            var failureId = rootChildren.Single(c => c.Condition == RunCondition.OnFailure).Id;
            var grandchildId = (await _ChildrenAsync(successId, ct)).Single().Id;

            await _ClaimRootAsync(persistence, rootId, ct);
            await _MarkTerminalAsync(persistence, rootId, JobStatus.Failed, ct);

            // Skip-only safety net over the failed parent's timed children. The return COUNT is deliberately not
            // asserted: BuildHost keeps the dead-node recovery bridge running even with background services disabled
            // (its own comment says so), and that bridge's unscoped set-based reconcile may perform the same skip
            // first. The durable row state below — not "which call did the skip" — is the contract.
            await persistence.SkipStrandedTimedChildrenAsync(ct);

            // AE9: the non-matching OnSuccess timed child and its whole subtree are skipped.
            (await _ReadNodeAsync(successId, ct))
                .Status.Should()
                .Be(JobStatus.Skipped);
            (await _ReadNodeAsync(grandchildId, ct)).Status.Should().Be(JobStatus.Skipped);
            // The skip side never terminalizes the matching OnFailure (Catch) child.
            (await _ReadNodeAsync(failureId, ct))
                .Status.Should()
                .Be(JobStatus.Idle);

            // Per-parent reconcile: releases the matching OnFailure (Catch) child.
            await persistence.ApplyParentTerminalRunConditionsAsync(rootId, ct);
            var failureRow = await _ReadNodeAsync(failureId, ct);
            failureRow.Status.Should().Be(JobStatus.Idle);
            failureRow.OwnerId.Should().BeNull();
            failureRow.ExecutionTime!.Value.Should().BeAfter(pastDue.AddMinutes(1));
            (await persistence.GetEarliestTimeJobsAsync(ct)).Should().Contain(x => x.Id == failureId);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // KTD7 (provider level). A chain root claimed by a now-dead node with a lapsed lease is reclaimed to Idle by the
    // stalled-lease sweep, its still-Idle children are left untouched (never prematurely skipped), and the chain is
    // resumable — the root re-surfaces as a claim candidate.
    public virtual async Task dead_node_reclaim_resumes_chain_without_skipping_children()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-reclaim");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var builder = JobChain.Start(_Payload("root"), executionTime: DateTime.UtcNow.AddSeconds(1));
            var child = builder.Root.Then(_Payload("child"));
            child.Then(_Payload("grandchild"));
            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);
            var childId = (await _ChildrenAsync(rootId, ct)).Single().Id;
            var grandchildId = (await _ChildrenAsync(childId, ct)).Single().Id;

            // Simulate a dead owner mid-chain: root InProgress with a lapsed lease (Retry policy from the default),
            // children never claimed (still Idle).
            await _ForceInProgressWithLapsedLeaseAsync(rootId, "dead-node@1", ct);

            (await persistence.ReclaimStalledTimeJobsAsync(ct))
                .Should()
                .BeGreaterThan(0, "the stalled-lease sweep must reclaim the dead owner's running root");

            var rootRow = await _ReadNodeAsync(rootId, ct);
            rootRow.Status.Should().Be(JobStatus.Idle);
            rootRow.OwnerId.Should().BeNull();

            // KTD7 guard at the provider level: reclaim never terminalizes the still-Idle children.
            (await _ReadNodeAsync(childId, ct))
                .Status.Should()
                .Be(JobStatus.Idle);
            (await _ReadNodeAsync(grandchildId, ct)).Status.Should().Be(JobStatus.Idle);

            (await persistence.GetEarliestTimeJobsAsync(ct))
                .Should()
                .Contain(x => x.Id == rootId, "the reclaimed chain root is resumable");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // ----- helpers -------------------------------------------------------------------------------------------------

    private static CoordinatedFacadeRequest _Payload(string tag) => new(Guid.NewGuid(), tag);

    private static IEnumerable<Guid> _FlattenIds(TimeJobEntity node)
    {
        yield return node.Id;
        foreach (var child in node.Children)
        {
            foreach (var id in _FlattenIds(child))
            {
                yield return id;
            }
        }
    }

    /// <summary>Polls the main peek until the root surfaces (absorbs coordination-membership warm-up), then returns it.</summary>
    private static async Task<TimeJobEntity[]> _PollEarliestUntilPresentAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid rootId,
        CancellationToken ct
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var candidates = await persistence.GetEarliestTimeJobsAsync(ct);
            if (Array.Exists(candidates, x => x.Id == rootId))
            {
                return candidates;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Chain root {rootId} never surfaced as a claim candidate.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
    }

    /// <summary>Claims the chain root through the native claim path (root -> Queued), returning the claimed root.</summary>
    private static async Task<TimeJobEntity> _ClaimRootAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid rootId,
        CancellationToken ct
    )
    {
        var candidates = await _PollEarliestUntilPresentAsync(persistence, rootId, ct);
        var claimed = await persistence.QueueTimeJobsAsync(candidates, ct).ToArrayAsync(ct);
        var root = Array.Find(claimed, x => x.Id == rootId);

        return root ?? throw new InvalidOperationException($"Chain root {rootId} was not claimed.");
    }

    /// <summary>Writes a terminal status onto the claimed row exactly as the executor's fenced completion does.</summary>
    private static async Task _MarkTerminalAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid id,
        JobStatus status,
        CancellationToken ct
    )
    {
        var completion = new JobExecutionState { FunctionName = "chain", JobId = id }.SetProperty(
            x => x.Status,
            status
        );
        (await persistence.UpdateTimeJobAsync(completion, ct))
            .Should()
            .Be(1, "the owning node's terminal write must land on the claimed row");
    }

    /// <summary>Polls the timed-out fallback until the child is claimed once its own execution time passes.</summary>
    private static async Task<TimeJobEntity?> _PollTimedOutUntilClaimedAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid childId,
        CancellationToken ct
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            var match = Array.Find(claimed, x => x.Id == childId);
            if (match is not null)
            {
                return match;
            }

            if (DateTime.UtcNow > deadline)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    private sealed record ChainNode(
        JobStatus Status,
        string? OwnerId,
        DateTime? LockedUntil,
        DateTime? ExecutionTime,
        Guid? ParentId,
        RunCondition? RunCondition,
        string? SkippedReason
    );

    private async Task<ChainNode> _ReadNodeAsync(Guid id, CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"Status\", \"OwnerId\", \"LockedUntil\", \"ExecutionTime\", \"ParentId\", \"RunCondition\", "
            + $"\"SkippedReason\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;";
        _AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException($"TimeJob {id} not found.");
        }

        var status = Enum.Parse<JobStatus>(reader.GetString(0));
        var ownerId = await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1);
        var lockedUntil = await reader.IsDBNullAsync(2, ct) ? (DateTime?)null : reader.GetDateTime(2);
        var executionTime = await reader.IsDBNullAsync(3, ct) ? (DateTime?)null : reader.GetDateTime(3);
        var parentId = await reader.IsDBNullAsync(4, ct) ? (Guid?)null : reader.GetGuid(4);
        var runCondition = await reader.IsDBNullAsync(5, ct)
            ? (RunCondition?)null
            : Enum.Parse<RunCondition>(reader.GetString(5));
        var skippedReason = await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6);

        return new ChainNode(status, ownerId, lockedUntil, executionTime, parentId, runCondition, skippedReason);
    }

    private async Task<IReadOnlyList<(Guid Id, RunCondition? Condition)>> _ChildrenAsync(
        Guid parentId,
        CancellationToken ct
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"Id\", \"RunCondition\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"ParentId\" = @parentId "
            + "ORDER BY \"RunCondition\";";
        _AddParameter(command, "@parentId", parentId);

        var children = new List<(Guid, RunCondition?)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var condition = await reader.IsDBNullAsync(1, ct)
                ? (RunCondition?)null
                : Enum.Parse<RunCondition>(reader.GetString(1));
            children.Add((id, condition));
        }

        return children;
    }

    /// <summary>Forces a row into InProgress with a lapsed lease under a foreign owner (simulates a dead node's claim).</summary>
    private async Task _ForceInProgressWithLapsedLeaseAsync(Guid id, string ownerId, CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"Status\" = @status, \"OwnerId\" = @ownerId, "
            + "\"LockedUntil\" = @lockedUntil, \"UpdatedAt\" = @lockedUntil WHERE \"Id\" = @id;";
        _AddParameter(command, "@status", JobStatus.InProgress.ToString());
        _AddParameter(command, "@ownerId", ownerId);
        _AddParameter(command, "@lockedUntil", DateTime.UtcNow.AddMinutes(-5));
        _AddParameter(command, "@id", id);

        await command.ExecuteNonQueryAsync(ct);
    }

    // Both Npgsql and SqlClient accept the "@name" parameter form; DateTime is written as kind-unspecified DateTime2.
    private static void _AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        if (value is DateTime dateTime)
        {
            parameter.DbType = DbType.DateTime2;
            parameter.Value = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        }
        else
        {
            parameter.Value = value;
        }

        command.Parameters.Add(parameter);
    }
}
