// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// The immediate-dispatch path — a chain root with NO execution time, which is the default for
    /// <see cref="IJobScheduler.EnqueueAsync(JobChain,System.Threading.CancellationToken)" /> — must lease the whole
    /// non-timed subtree, not just the root. The executor runs a claimed chain by in-process recursion and fences
    /// every node on lease renewal before invoking it, so a hydrated-but-unleased descendant fails that fence and is
    /// stranded Idle forever. Every other scenario here uses a timed root, which takes the scheduled tree claim, so
    /// this is the only coverage of the acquire path's own lease walk.
    /// </summary>
    public virtual async Task immediate_acquire_leases_the_whole_non_timed_subtree()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-immediate");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // No executionTime anywhere: the root is immediately due and every descendant is a non-timed continuation.
            var builder = JobChain.Start(_Payload("root"));
            var child = builder.Root.Then(_Payload("child"));
            child.Then(_Payload("grandchild"));

            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var rootId = await scheduler.EnqueueAsync(builder.Build(), ct);
            var childId = (await _ChildrenAsync(rootId, ct)).Single().Id;
            var grandchildId = (await _ChildrenAsync(childId, ct)).Single().Id;

            var acquired = await persistence.AcquireImmediateTimeJobsAsync([rootId], ct);
            acquired.Should().ContainSingle(x => x.Id == rootId);

            var rootRow = await _ReadNodeAsync(rootId, ct);
            rootRow.Status.Should().Be(JobStatus.InProgress);
            rootRow.OwnerId.Should().NotBeNull();

            // Both descendants must carry the SAME owner and the root's EXACT deadline (KTD2 invariant 2: descendants
            // copy the root's persisted LockedUntil rather than re-reading a per-statement clock).
            var childRow = await _ReadNodeAsync(childId, ct);
            childRow.OwnerId.Should().Be(rootRow.OwnerId, "an unleased child fails the executor's renewal fence");
            childRow.LockedUntil.Should().Be(rootRow.LockedUntil, "descendants share the root's exact lease deadline");

            var grandchildRow = await _ReadNodeAsync(grandchildId, ct);
            grandchildRow.OwnerId.Should().Be(rootRow.OwnerId, "the lease walk must reach the whole subtree");
            grandchildRow.LockedUntil.Should().Be(rootRow.LockedUntil);

            // Leased descendants stay Idle — the executor transitions each one as it recurses into it.
            childRow.Status.Should().Be(JobStatus.Idle);
            grandchildRow.Status.Should().Be(JobStatus.Idle);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// R2/KTD3/KTD6: the poll-time safety-net sweep bounds its SELECTION at one batch, so a large stranded backlog
    /// drains monotonically across sweeps — and a full page of MATCHING (release-side) children, which this skip-only
    /// path never mutates, can never fill the page and starve the mismatched rows it must skip. Seeds a backlog wider
    /// than the batch cap (impossible through <see cref="JobChain"/>, which allows only two children per node) and
    /// does not start the host, so the dead-node recovery bridge's own unbounded reconcile cannot confound the count.
    /// </summary>
    public virtual async Task bounded_sweep_drains_a_large_mismatched_backlog_without_starving_on_matching_children()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-sweep-bound");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        const int cap = JobsClaimStrategyDefaults.MaxClaimBatchSize;
        var pastDue = DateTime.UtcNow.AddMinutes(-5);

        // Parent terminalizes as Failed: OnSuccess children mismatch (the skip side), OnFailure children match (the
        // release side the skip-only sweep must never touch).
        var parent = _NewJob("sweep-parent", executionTime: pastDue);
        var mismatched = Enumerable
            .Range(0, cap + 5)
            .Select(_ => _NewChild(parent.Id, RunCondition.OnSuccess, pastDue))
            .ToArray();
        var matching = Enumerable
            .Range(0, 10)
            .Select(_ => _NewChild(parent.Id, RunCondition.OnFailure, pastDue))
            .ToArray();

        await persistence.AddTimeJobsAsync([parent, .. mismatched, .. matching], ct);
        await _SetStatusAsync(parent.Id, JobStatus.Failed, ct);

        // First sweep is bounded to exactly one batch of mismatched leaves (no subtree, so no cascade).
        var firstSkip = await persistence.SkipStrandedTimedChildrenAsync(ct);
        firstSkip.Should().Be(cap, "the sweep bounds its selection at one batch, not the whole backlog");

        var total = firstSkip;
        for (var i = 0; i < 5 && total < mismatched.Length; i++)
        {
            total += await persistence.SkipStrandedTimedChildrenAsync(ct);
        }

        total.Should().Be(mismatched.Length, "every mismatched child drains across successive sweeps");

        foreach (var m in mismatched)
        {
            (await _ReadNodeAsync(m.Id, ct)).Status.Should().Be(JobStatus.Skipped);
        }

        foreach (var m in matching)
        {
            (await _ReadNodeAsync(m.Id, ct))
                .Status.Should()
                .Be(JobStatus.Idle, "matching children are the release side; the skip-only sweep never touches them");
        }
    }

    /// <summary>
    /// KTD6: the sweep bounds only its SELECTION; the subtree cascade under a skipped mismatched child is UNCAPPED.
    /// Capping the cascade would strand the non-timed descendants — they are not sweep candidates themselves
    /// (ExecutionTime is null), so no later sweep would ever re-select them — so a single sweep must skip a mismatched
    /// child's whole subtree in one pass.
    /// </summary>
    public virtual async Task bounded_sweep_skips_a_mismatched_child_whole_subtree_in_one_pass()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-sweep-cascade");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var pastDue = DateTime.UtcNow.AddMinutes(-5);

        var parent = _NewJob("cascade-parent", executionTime: pastDue);
        // One mismatched timed child (OnSuccess under a Failed parent) rooting a WIDE non-timed subtree.
        var mismatchedChild = _NewChild(parent.Id, RunCondition.OnSuccess, pastDue);
        var subtree = Enumerable
            .Range(0, 25)
            .Select(_ => _NewChild(mismatchedChild.Id, RunCondition.OnSuccess, executionTime: null))
            .ToArray();

        await persistence.AddTimeJobsAsync([parent, mismatchedChild, .. subtree], ct);
        await _SetStatusAsync(parent.Id, JobStatus.Failed, ct);

        var skipped = await persistence.SkipStrandedTimedChildrenAsync(ct);
        skipped
            .Should()
            .Be(1 + subtree.Length, "the direct mismatch plus its whole uncapped subtree skip in one sweep");

        (await _ReadNodeAsync(mismatchedChild.Id, ct)).Status.Should().Be(JobStatus.Skipped);
        foreach (var d in subtree)
        {
            (await _ReadNodeAsync(d.Id, ct)).Status.Should().Be(JobStatus.Skipped, "the cascade is uncapped");
        }
    }

    /// <summary>
    /// R4/KTD4 (CAS frontier fence). The generic-EF tree claim replaced the plan's single claim transaction with
    /// fenced autocommit statements: after the root is stamped, each descendant lease UPDATE re-asserts
    /// <c>EXISTS(root still owned by me AND lease unexpired)</c>. If that root ownership is lost mid-walk — the lease
    /// lapses, or another node steals the root — the fence must reject the descendants so no orphaned tail is leased
    /// (split ownership). Driven by the KTD4 seam so lease loss is a deterministic state change, never a race against
    /// statement latency. Two cases: (i) the root lease expires; (ii) the root is reassigned to another owner.
    /// </summary>
    public virtual async Task cas_frontier_fence_rejects_descendants_when_root_lease_expires_mid_walk()
    {
        await _RunCasFenceCaseAsync(
            async (fixtureConn, rootId) =>
            {
                // Case (i): expire the claimed root's lease so EXISTS(... LockedUntil > now) fails.
                await _SqlAsync(
                    fixtureConn,
                    $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"LockedUntil\" = @past WHERE \"Id\" = @id;",
                    ("@past", DateTime.UtcNow.AddMinutes(-5)),
                    ("@id", rootId)
                );
            }
        );
    }

    /// <inheritdoc cref="cas_frontier_fence_rejects_descendants_when_root_lease_expires_mid_walk"/>
    public virtual async Task cas_frontier_fence_rejects_descendants_when_root_is_stolen_mid_walk()
    {
        await _RunCasFenceCaseAsync(
            async (fixtureConn, rootId) =>
            {
                // Case (ii): reassign the claimed root to a different owner so EXISTS(... OwnerId = me) fails.
                await _SqlAsync(
                    fixtureConn,
                    $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"OwnerId\" = @thief WHERE \"Id\" = @id;",
                    ("@thief", "thief@9"),
                    ("@id", rootId)
                );
            }
        );
    }

    private async Task _RunCasFenceCaseAsync(Func<DbConnection, Guid, Task> invalidateRoot)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("cas-fence");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        // Started so the peek's owner gate is satisfied; background services are disabled and the recovery bridge only
        // reclaims coordination-reported dead nodes, never our fixed cas@1 owner, so nothing races the claim.
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // Root due imminently so the staleness-filtered main peek surfaces it (mirrors the other chain tests, which
            // use AddSeconds(1)); a far-past ExecutionTime would route to the fallback claim instead of the peek.
            var root = _NewJob("cas-root", DateTime.UtcNow.AddSeconds(1));
            var child = _NewChild(root.Id, RunCondition.OnSuccess, executionTime: null);
            var grandchild = _NewChild(child.Id, RunCondition.OnSuccess, executionTime: null);
            await persistence.AddTimeJobsAsync([root, child, grandchild], ct);

            var candidates = await _PollEarliestUntilPresentAsync(persistence, root.Id, ct);

            // The seam fires between the root claim and the first descendant lease and invalidates root ownership.
            var cas = _BuildCas(
                host,
                "cas@1",
                async () =>
                {
                    await using var seamConnection = fixture.CreateConnection();
                    await seamConnection.OpenAsync(ct);
                    await invalidateRoot(seamConnection, root.Id);
                }
            );

            var claimed = await cas.ClaimTimeJobsAsync(candidates, ct).ToArrayAsync(ct);

            // The root was stamped before the seam, so it is still returned — but pruned to itself, with NO leased tail.
            var claimedRoot = claimed.Should().ContainSingle(x => x.Id == root.Id).Subject;
            claimedRoot.Children.Should().BeEmpty("the fence rejected every descendant once root ownership was lost");

            // The decisive contract: no descendant was leased. No split ownership.
            var childRow = await _ReadNodeAsync(child.Id, ct);
            childRow.Status.Should().Be(JobStatus.Idle);
            childRow.OwnerId.Should().BeNull("a descendant must never be leased when the root fence fails");

            var grandchildRow = await _ReadNodeAsync(grandchild.Id, ct);
            grandchildRow.Status.Should().Be(JobStatus.Idle);
            grandchildRow.OwnerId.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// R4 (root CAS + PruneToClaimedSet). Two owners race the same root through the CAS tree claim. The root's
    /// optimistic <c>UpdatedAt</c> gate lets exactly one win; the loser's root UPDATE affects zero rows and it claims
    /// nothing — no node is split across owners. Scoped to the root CAS and the claimed-set pruning; the descendant
    /// fence is exercised separately above.
    /// </summary>
    public virtual async Task two_owner_root_race_leaves_no_split_ownership()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("cas-race");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var root = _NewJob("race-root", DateTime.UtcNow.AddSeconds(1));
            var child = _NewChild(root.Id, RunCondition.OnSuccess, executionTime: null);
            await persistence.AddTimeJobsAsync([root, child], ct);

            // Each owner peeks its OWN candidate snapshot — same persisted UpdatedAt, but distinct objects. This
            // mirrors production (each poller reads its own array) and matters because ClaimTimeJobsAsync mutates the
            // candidate's UpdatedAt in place on a win; sharing one array would leak the winner's new value into the
            // loser's optimistic gate and let both "win".
            var candidatesA = await _PollEarliestUntilPresentAsync(persistence, root.Id, ct);
            var candidatesB = await _PollEarliestUntilPresentAsync(persistence, root.Id, ct);

            var ownerA = _BuildCas(host, "owner-a@1");
            var ownerB = _BuildCas(host, "owner-b@1");

            var claimedByA = await ownerA.ClaimTimeJobsAsync(candidatesA, ct).ToArrayAsync(ct);
            var claimedByB = await ownerB.ClaimTimeJobsAsync(candidatesB, ct).ToArrayAsync(ct);

            (claimedByA.Length + claimedByB.Length)
                .Should()
                .Be(1, "the root's optimistic UpdatedAt gate lets exactly one owner claim it");

            var rootRow = await _ReadNodeAsync(root.Id, ct);
            rootRow.OwnerId.Should().BeOneOf("owner-a@1", "owner-b@1");

            var childRow = await _ReadNodeAsync(child.Id, ct);
            childRow.OwnerId.Should().Be(rootRow.OwnerId, "the child must belong to the same owner that won the root");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// R4 (native-CTE contention). Two nodes race the same ≥2-node chain root through the fixtures' NORMAL strategy
    /// selection — i.e. the provider's recursive-CTE claim (<c>PostgreSqlJobsClaimStrategy</c> /
    /// <c>SqlServerJobsClaimStrategy</c>), not the generic-EF frontier. The CTE claim is one atomic statement, so
    /// exactly one node wins the root AND its whole descendant subtree in a single instant; the loser claims nothing,
    /// no node is split across owners, and every claimed descendant carries the winner's EXACT persisted lease
    /// deadline (KTD2 invariant 2). Two hosts (distinct nodes, shared database) drive it over the public surface.
    /// </summary>
    public virtual async Task native_claim_contention_gives_one_owner_the_whole_subtree()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var hostA = fixture.BuildHost("native-a");
        using var hostB = fixture.BuildHost("native-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(hostA, ct);
        await hostA.StartAsync(ct);
        await hostB.StartAsync(ct);

        try
        {
            var persistenceA = hostA.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            var persistenceB = hostB.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();

            var root = _NewJob("native-root", DateTime.UtcNow.AddSeconds(1));
            var child = _NewChild(root.Id, RunCondition.OnSuccess, executionTime: null);
            var grandchild = _NewChild(child.Id, RunCondition.OnSuccess, executionTime: null);
            await persistenceA.AddTimeJobsAsync([root, child, grandchild], ct);

            // Each node peeks its own snapshot (same persisted UpdatedAt, distinct objects) before either claims.
            var candA = await _PollEarliestUntilPresentAsync(persistenceA, root.Id, ct);
            var candB = await _PollEarliestUntilPresentAsync(persistenceB, root.Id, ct);

            var claimedA = await persistenceA.QueueTimeJobsAsync(candA, ct).ToArrayAsync(ct);
            var claimedB = await persistenceB.QueueTimeJobsAsync(candB, ct).ToArrayAsync(ct);

            (claimedA.Length + claimedB.Length)
                .Should()
                .Be(1, "the recursive-CTE claim's root CAS lets exactly one node win");

            var rootRow = await _ReadNodeAsync(root.Id, ct);
            rootRow.OwnerId.Should().NotBeNullOrWhiteSpace();

            // The winner took the WHOLE subtree in one CTE statement — no descendant split across owners.
            var childRow = await _ReadNodeAsync(child.Id, ct);
            childRow.OwnerId.Should().Be(rootRow.OwnerId, "the child belongs to the node that won the root");
            childRow.LockedUntil.Should().Be(rootRow.LockedUntil, "descendants carry the root's exact lease deadline");

            var grandchildRow = await _ReadNodeAsync(grandchild.Id, ct);
            grandchildRow.OwnerId.Should().Be(rootRow.OwnerId);
            grandchildRow.LockedUntil.Should().Be(rootRow.LockedUntil);
        }
        finally
        {
            await hostB.StopAsync(ct);
            await hostA.StopAsync(ct);
        }
    }

    /// <summary>
    /// U5/KTD3 native-SQL gate parity. Replays the shared gate grid (every <see cref="RunCondition"/> including the
    /// non-gated ones and <see langword="null"/> × every terminal status plus a non-terminal control) against the
    /// provider's <c>TimedChildGateSql</c>, which is embedded in the native timed-out fallback claim. A past-due timed
    /// child is claimed by that path iff the native SQL gate admits it, so the claimed set must equal the
    /// <see cref="ChainRunConditionRules"/> expectation the LINQ and in-memory implementations also match (the unit
    /// matrix asserts those). Any drift between the hand-written SQL and the shared rules fails here.
    /// </summary>
    public virtual async Task native_sql_gate_matches_the_shared_rules_across_the_grid()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("gate-grid");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var pastDue = DateTime.UtcNow.AddMinutes(-5);

            RunCondition?[] conditions =
            [
                null,
                RunCondition.InProgress,
                RunCondition.OnSuccess,
                RunCondition.OnFailure,
                RunCondition.OnCancelled,
                RunCondition.OnFailureOrCancelled,
                RunCondition.OnAnyCompletedStatus,
            ];
            JobStatus[] statuses =
            [
                JobStatus.Succeeded,
                JobStatus.DueDone,
                JobStatus.Failed,
                JobStatus.Cancelled,
                JobStatus.Skipped,
                JobStatus.Queued,
            ];

            var seed = new List<TimeJobEntity>();
            var expectedClaimed = new Dictionary<Guid, bool>();

            foreach (var status in statuses)
            {
                foreach (var condition in conditions)
                {
                    // Parent is non-timed (never a fallback candidate itself) and carries the grid status.
                    var parent = _NewJob("grid-parent", executionTime: null);
                    parent.Status = status;

                    // Child is an idle, past-due timed descendant carrying the grid run condition.
                    var child = _NewJob("grid-child", pastDue);
                    child.ParentId = parent.Id;
                    child.RunCondition = condition;

                    seed.Add(parent);
                    seed.Add(child);

                    var gated = ChainRunConditionRules.IsParentTerminalGated(condition);
                    expectedClaimed[child.Id] =
                        !gated || ChainRunConditionRules.ParentTerminalMatches(condition, status);
                }
            }

            await persistence.AddTimeJobsAsync([.. seed], ct);

            // The native timed-out fallback claim embeds TimedChildGateSql; a child is claimed iff the gate admits it.
            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            var claimedIds = claimed.Select(x => x.Id).ToHashSet();

            foreach (var (childId, expected) in expectedClaimed)
            {
                claimedIds.Contains(childId).Should().Be(expected, "native-SQL gate parity for child {0}", childId);
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// R9. A timed descendant whose parent reached <see cref="JobStatus.Skipped" /> must itself be skipped by the
    /// safety net — <c>Skipped</c> is a terminal state that satisfies no run condition, so a gated timed child under a
    /// skipped parent is mismatched and swept, never stranded Idle. This is the storage-visible tail of a non-timed
    /// sibling being skipped: its timed descendant follows it to Skipped.
    /// </summary>
    public virtual async Task timed_child_of_a_skipped_parent_is_swept_to_skipped()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("skipped-parent");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var pastDue = DateTime.UtcNow.AddMinutes(-5);

        // A non-timed sibling already skipped (as the executor's cascade would leave it), carrying a timed OnSuccess
        // descendant that is still Idle and past-due.
        var skippedParent = _NewJob("skipped-parent", executionTime: null);
        skippedParent.Status = JobStatus.Skipped;
        var timedChild = _NewChild(skippedParent.Id, RunCondition.OnSuccess, pastDue);
        var grandchild = _NewChild(timedChild.Id, RunCondition.OnSuccess, executionTime: null);
        await persistence.AddTimeJobsAsync([skippedParent, timedChild, grandchild], ct);

        await persistence.SkipStrandedTimedChildrenAsync(ct);

        (await _ReadNodeAsync(timedChild.Id, ct))
            .Status.Should()
            .Be(JobStatus.Skipped, "a timed child under a Skipped parent matches no run condition and is swept");
        (await _ReadNodeAsync(grandchild.Id, ct))
            .Status.Should()
            .Be(JobStatus.Skipped, "and its subtree cascades to Skipped with it");
    }

    /// <summary>
    /// R10/R8. A chain persisted while the depth limit was higher, then claimed under a LOWER
    /// <c>MaxChainDepth</c> (the default 10 here), truncates: the claim leases root..depth-10 and leaves deeper nodes
    /// Idle rather than erroring. Seeded directly because <c>EnqueueAsync</c> rejects an over-depth chain — this is the
    /// "limit lowered after enqueue" case. Also pins the SqlServer recursive-CTE <c>MAXRECURSION</c> boundary: the
    /// claim of an over-depth persisted chain completes (the CTE self-limits at MaxChainDepth) instead of raising 530.
    /// </summary>
    public virtual async Task deep_chain_claim_truncates_at_configured_depth_without_erroring()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("depth-truncate");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            const int maxChainDepth = SchedulerOptionsBuilder.DefaultMaxChainDepth; // 10

            // A 13-node linear chain (root = depth 1) — deeper than the configured limit. Root is timed so the peek
            // surfaces it; every descendant is a non-timed continuation.
            var nodes = new List<TimeJobEntity> { _NewJob("d1", DateTime.UtcNow.AddSeconds(1)) };
            for (var depth = 2; depth <= 13; depth++)
            {
                nodes.Add(_NewChild(nodes[^1].Id, RunCondition.OnSuccess, executionTime: null));
            }
            await persistence.AddTimeJobsAsync([.. nodes], ct);

            var candidates = await _PollEarliestUntilPresentAsync(persistence, nodes[0].Id, ct);
            var claimed = await persistence.QueueTimeJobsAsync(candidates, ct).ToArrayAsync(ct);
            claimed.Should().Contain(x => x.Id == nodes[0].Id, "the claim completes without a MAXRECURSION error");

            var rootRow = await _ReadNodeAsync(nodes[0].Id, ct);
            rootRow.OwnerId.Should().NotBeNullOrWhiteSpace();

            for (var depth = 1; depth <= 13; depth++)
            {
                var row = await _ReadNodeAsync(nodes[depth - 1].Id, ct);
                if (depth <= maxChainDepth)
                {
                    row.OwnerId.Should().Be(rootRow.OwnerId, "node at depth {0} is within the limit and leased", depth);
                }
                else
                {
                    row.OwnerId.Should().BeNull("node at depth {0} is beyond the limit and left Idle", depth);
                    row.Status.Should().Be(JobStatus.Idle);
                }
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private EfCoreCasJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity> _BuildCas(
        Microsoft.Extensions.Hosting.IHost host,
        string owner,
        Func<Task>? onFrontierBeforeLease = null
    )
    {
        var factory = host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>();
        var timeProvider = host.Services.GetRequiredService<TimeProvider>();
        var guidGenerator = host.Services.GetRequiredService<IGuidGenerator>();
        var options = host.Services.GetRequiredService<SchedulerOptionsBuilder>();

        return new EfCoreCasJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>(
            factory,
            timeProvider,
            guidGenerator,
            new _FixedOwnerIdentity(owner),
            options
        )
        {
            OnFrontierBeforeLease = onFrontierBeforeLease,
        };
    }

    private static async Task _SqlAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            JobsCoordinationFixtureExtensions.AddParameter(command, name, value);
        }
        await command.ExecuteNonQueryAsync();
    }

    // Fixed owner for the CAS-strategy tests: the coordination-backed IJobsOwnerIdentity only yields an owner once
    // membership is established (host started), which we deliberately avoid so the recovery bridge cannot race the
    // claim under test. This stands in with a stable owner and never-lost membership.
    private sealed class _FixedOwnerIdentity(string owner) : IJobsOwnerIdentity
    {
        public string DisplayOwner => owner;

        public bool TryGetStampOwner([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? stampOwner)
        {
            stampOwner = owner;
            return true;
        }

        public CancellationToken MembershipLostToken => CancellationToken.None;
    }

    // ----- helpers -------------------------------------------------------------------------------------------------

    private static CoordinatedFacadeRequest _Payload(string tag) => new(Guid.NewGuid(), tag);

    // Directly-seeded root row (ParentId == null). Bypasses JobChain so a test can seed a subtree wider than two
    // children per node — the shape the bounded sweep needs but the builder cannot express.
    private static TimeJobEntity _NewJob(string function, DateTime? executionTime)
    {
        var now = DateTime.UtcNow;
        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = function,
            Status = JobStatus.Idle,
            ExecutionTime = executionTime,
            OnNodeDeath = NodeDeathPolicy.Retry,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static TimeJobEntity _NewChild(Guid parentId, RunCondition condition, DateTime? executionTime)
    {
        var child = _NewJob("child", executionTime);
        child.ParentId = parentId;
        child.RunCondition = condition;
        return child;
    }

    // Flips a seeded row's status directly, standing in for a fenced executor completion without needing a running
    // host (whose recovery bridge would race the sweep under test).
    private async Task _SetStatusAsync(Guid id, JobStatus status, CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"Status\" = @status, \"UpdatedAt\" = @updatedAt WHERE \"Id\" = @id;";
        JobsCoordinationFixtureExtensions.AddParameter(command, "@status", status.ToString());
        JobsCoordinationFixtureExtensions.AddParameter(command, "@updatedAt", DateTime.UtcNow);
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);

        await command.ExecuteNonQueryAsync(ct);
    }

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
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);

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
        JobsCoordinationFixtureExtensions.AddParameter(command, "@parentId", parentId);

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
        JobsCoordinationFixtureExtensions.AddParameter(command, "@status", JobStatus.InProgress.ToString());
        JobsCoordinationFixtureExtensions.AddParameter(command, "@ownerId", ownerId);
        JobsCoordinationFixtureExtensions.AddParameter(command, "@lockedUntil", DateTime.UtcNow.AddMinutes(-5));
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);

        await command.ExecuteNonQueryAsync(ct);
    }
}
