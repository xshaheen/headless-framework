// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// U3: the in-memory provider claims and hydrates a chain to the configured <c>MaxChainDepth</c> — beyond the
/// old fixed grandchild cap — carrying the full field set at every level, and adds a chain tree all-or-nothing
/// (KTD6). Cross-provider parity is proved in the EF harness (U7).
/// </summary>
public sealed class DeepChainProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private static readonly DateTime _Now = new(2026, 06, 17, 12, 00, 00, DateTimeKind.Utc);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create(
        int maxChainDepth = SchedulerOptionsBuilder.DefaultMaxChainDepth
    )
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(
            new SchedulerOptionsBuilder
            {
                NodeId = _NodeA,
                LeaseDuration = _Lease,
                MaxChainDepth = maxChainDepth,
            }
        );
        var sp = services.BuildServiceProvider();
        return (new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp), time);
    }

    /// <summary>
    /// Builds a linear chain <paramref name="depth"/> nodes long. The root carries an execution time inside the
    /// main-scheduler window; every descendant is a non-timed <c>OnSuccess</c> child with a distinct
    /// <c>RetryCount</c>/<c>OnNodeDeath</c> so a projection that drops a level or a field is caught.
    /// </summary>
    private static FakeTimeJob[] _LinearChain(int depth)
    {
        var nodes = new FakeTimeJob[depth];
        for (var level = 0; level < depth; level++)
        {
            var isRoot = level == 0;
            nodes[level] = new FakeTimeJob
            {
                Id = Guid.NewGuid(),
                Function = $"fn-{level}",
                Status = JobStatus.Idle,
                RetryCount = level, // distinct per level — a level-dropping projection resets it
                OnNodeDeath = level % 2 == 0 ? NodeDeathPolicy.Retry : NodeDeathPolicy.MarkFailed,
                ExecutionTime = isRoot ? _Now.AddMilliseconds(500) : null,
                RunCondition = isRoot ? null : RunCondition.OnSuccess,
                ParentId = isRoot ? null : nodes[level - 1].Id,
            };
        }

        return nodes;
    }

    [Fact]
    public async Task queue_time_jobs_hydrates_and_leases_the_full_chain_depth()
    {
        var (provider, _) = _Create();
        var chain = _LinearChain(depth: 5);
        await provider.AddTimeJobsAsync(chain, AbortToken);
        var roots = await provider.GetEarliestTimeJobsAsync(AbortToken);

        TimeJobEntity? claimed = null;
        await foreach (var job in provider.QueueTimeJobsAsync(roots, AbortToken))
        {
            claimed = job;
        }

        // The hydrated tree reaches the fifth node — past the old grandchild cap — with each level's own fields.
        claimed.Should().NotBeNull();
        TimeJobEntity? node = claimed;
        for (var level = 0; level < chain.Length; level++)
        {
            node.Should().NotBeNull($"level {level} must be hydrated");
            node!.Id.Should().Be(chain[level].Id);
            node.Function.Should().Be($"fn-{level}");
            node.RetryCount.Should().Be(level);
            node.OnNodeDeath.Should().Be(chain[level].OnNodeDeath);
            if (level > 0)
            {
                node.RunCondition.Should().Be(RunCondition.OnSuccess);
                node.ParentId.Should().Be(chain[level - 1].Id);
            }

            node = node.Children.SingleOrDefault();
        }

        // Every descendant is pre-leased with the root claim (owned, future lease deadline).
        foreach (var seeded in chain.Skip(1))
        {
            var stored = await provider.GetTimeJobByIdAsync(seeded.Id, AbortToken);
            stored!.OwnerId.Should().Be(_NodeA);
            stored.LockedUntil.Should().Be(_Now.Add(_Lease));
        }
    }

    [Fact]
    public async Task queue_timed_out_time_jobs_hydrates_the_full_chain_depth()
    {
        var (provider, _) = _Create();
        var chain = _LinearChain(depth: 5);
        // Age the root past the fallback window so the timed-out path claims it.
        chain[0].ExecutionTime = _Now.AddMinutes(-2);
        await provider.AddTimeJobsAsync(chain, AbortToken);

        var claimed = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        var node = claimed.Should().ContainSingle().Subject;
        for (var level = 0; level < chain.Length; level++)
        {
            node.Should().NotBeNull($"level {level} must be hydrated by the timed-out path");
            node!.Id.Should().Be(chain[level].Id);
            node = node.Children.SingleOrDefault();
        }
    }

    [Fact]
    public async Task hydration_and_claim_stop_at_the_configured_max_chain_depth()
    {
        // MaxChainDepth 2 = root + one child level only; the grandchild the old fixed cap always hydrated must now
        // be absent AND left unclaimed.
        var (provider, _) = _Create(maxChainDepth: 2);
        var chain = _LinearChain(depth: 4);
        await provider.AddTimeJobsAsync(chain, AbortToken);
        var roots = await provider.GetEarliestTimeJobsAsync(AbortToken);

        TimeJobEntity? claimed = null;
        await foreach (var job in provider.QueueTimeJobsAsync(roots, AbortToken))
        {
            claimed = job;
        }

        claimed!.Id.Should().Be(chain[0].Id);
        var child = claimed.Children.Should().ContainSingle().Subject;
        child.Id.Should().Be(chain[1].Id);
        child.Children.Should().BeEmpty("depth 2 stops one level below the root");

        // The out-of-bounds grandchild is neither hydrated nor leased.
        var grandChild = await provider.GetTimeJobByIdAsync(chain[2].Id, AbortToken);
        grandChild!.OwnerId.Should().BeNull();
        grandChild.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task claim_stops_at_a_non_idle_intermediate_node_and_prunes_its_tail()
    {
        // KTD2: a mid-chain node terminalized by a sweep (here c2 = Succeeded) is a claim boundary. The claim must not
        // lease nodes below it, and the returned tree must be rebuilt strictly from the claimed set — so the tail
        // never executes unclaimed.
        var (provider, _) = _Create();
        var root = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "root",
            Status = JobStatus.Idle,
            ExecutionTime = _Now.AddMilliseconds(500),
        };
        var c1 = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "c1",
            Status = JobStatus.Idle,
            RunCondition = RunCondition.OnSuccess,
            ParentId = root.Id,
        };
        var c2 = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "c2",
            Status = JobStatus.Succeeded, // terminalized mid-chain
            RunCondition = RunCondition.OnSuccess,
            ParentId = c1.Id,
        };
        var c3 = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "c3",
            Status = JobStatus.Idle,
            RunCondition = RunCondition.OnSuccess,
            ParentId = c2.Id,
        };
        await provider.AddTimeJobsAsync([root, c1, c2, c3], AbortToken);
        var roots = await provider.GetEarliestTimeJobsAsync(AbortToken);

        TimeJobEntity? claimed = null;
        await foreach (var job in provider.QueueTimeJobsAsync(roots, AbortToken))
        {
            claimed = job;
        }

        // The claimed tree stops at c1; c2 (non-idle) and its tail c3 are pruned.
        var child = claimed!.Children.Should().ContainSingle().Subject;
        child.Id.Should().Be(c1.Id);
        child.Children.Should().BeEmpty("the claim stopped at the terminalized c2");

        // c3 (below the non-idle frontier) is never leased.
        var storedC3 = await provider.GetTimeJobByIdAsync(c3.Id, AbortToken);
        storedC3!.OwnerId.Should().BeNull();
        storedC3.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task add_time_jobs_with_a_duplicate_id_in_the_tree_adds_nothing()
    {
        // KTD6 all-or-nothing: a duplicate id anywhere in the tree must leave NO row visible (validate before mutate),
        // not a partially-added parent.
        var (provider, _) = _Create();
        var root = new FakeTimeJob { Id = Guid.NewGuid(), Function = "root" };
        var child = new FakeTimeJob
        {
            Id = root.Id,
            Function = "child",
            RunCondition = RunCondition.OnSuccess,
        };
        root.Children = [child];

        var added = await provider.AddTimeJobsAsync([root], AbortToken);

        added.Should().Be(0);
        (await provider.GetTimeJobByIdAsync(root.Id, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task add_time_jobs_colliding_with_an_existing_row_leaves_the_new_tree_unadded()
    {
        // KTD6: a child id that collides with an already-stored row rejects the WHOLE new tree — the fresh root must
        // not become visible on its own.
        var (provider, _) = _Create();
        var existing = new FakeTimeJob { Id = Guid.NewGuid(), Function = "existing" };
        await provider.AddTimeJobsAsync([existing], AbortToken);

        var root = new FakeTimeJob { Id = Guid.NewGuid(), Function = "root" };
        var colliding = new FakeTimeJob
        {
            Id = existing.Id,
            Function = "colliding",
            RunCondition = RunCondition.OnSuccess,
        };
        root.Children = [colliding];

        var added = await provider.AddTimeJobsAsync([root], AbortToken);

        added.Should().Be(0);
        (await provider.GetTimeJobByIdAsync(root.Id, AbortToken)).Should().BeNull();
        // The pre-existing row is untouched.
        (await provider.GetTimeJobByIdAsync(existing.Id, AbortToken))!
            .Function.Should()
            .Be("existing");
    }

    [Fact]
    public async Task add_time_jobs_rejects_the_whole_call_when_a_later_root_collides_with_an_earlier_root_subtree()
    {
        // KTD6 cross-root all-or-nothing: two roots in ONE call, where the second root's id collides with a DESCENDANT
        // of the first. The whole call must be rejected — the first root and its child must NOT become visible. The old
        // per-root loop committed the first root's subtree before the second root failed, stranding it.
        var (provider, _) = _Create();
        var firstRoot = new FakeTimeJob { Id = Guid.NewGuid(), Function = "first-root" };
        var firstChild = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "first-child",
            RunCondition = RunCondition.OnSuccess,
        };
        firstRoot.Children = [firstChild];
        var secondRoot = new FakeTimeJob { Id = firstChild.Id, Function = "second-root" };

        var added = await provider.AddTimeJobsAsync([firstRoot, secondRoot], AbortToken);

        added.Should().Be(0);
        (await provider.GetTimeJobByIdAsync(firstRoot.Id, AbortToken)).Should().BeNull();
        (await provider.GetTimeJobByIdAsync(firstChild.Id, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task add_time_jobs_with_a_valid_root_and_a_root_colliding_with_existing_state_adds_nothing()
    {
        // KTD6 cross-root all-or-nothing: a call carrying a valid root AND a root that collides with an already-stored
        // row must reject the WHOLE call — the valid root must NOT become visible (the old per-root loop committed it
        // before the colliding root failed). The pre-existing row is untouched.
        var (provider, _) = _Create();
        var existing = new FakeTimeJob { Id = Guid.NewGuid(), Function = "existing" };
        await provider.AddTimeJobsAsync([existing], AbortToken);

        var validRoot = new FakeTimeJob { Id = Guid.NewGuid(), Function = "valid-root" };
        var collidingRoot = new FakeTimeJob { Id = existing.Id, Function = "colliding-root" };

        var added = await provider.AddTimeJobsAsync([validRoot, collidingRoot], AbortToken);

        added.Should().Be(0);
        (await provider.GetTimeJobByIdAsync(validRoot.Id, AbortToken)).Should().BeNull();
        (await provider.GetTimeJobByIdAsync(existing.Id, AbortToken))!.Function.Should().Be("existing");
    }

    [Fact]
    public async Task add_time_jobs_leaves_no_publication_barrier_state_on_committed_rows()
    {
        // KTD6 publication barrier: rows are parked (Status=InProgress + far-future synthetic lease) only WHILE the
        // batch installs; once AddTimeJobsAsync returns, every row must carry its authored state — no leftover barrier
        // InProgress and no synthetic lease — so the batch is claimable exactly as written.
        var (provider, _) = _Create();
        var chain = _LinearChain(depth: 4);

        await provider.AddTimeJobsAsync(chain, AbortToken);

        foreach (var node in chain)
        {
            var stored = await provider.GetTimeJobByIdAsync(node.Id, AbortToken);
            stored!.Status.Should().Be(JobStatus.Idle, "the barrier InProgress state is cleared on reveal");
            stored.LockedUntil.Should().BeNull("the synthetic barrier lease is cleared on reveal");
            stored.OwnerId.Should().BeNull();
        }
    }

    [Fact]
    public void sync_reconcile_candidate_converges_on_the_live_row_not_the_passed_value()
    {
        // Version-aware index sync: _SyncReconcileCandidate must reconcile the candidate index against the CURRENT live
        // row, not the (possibly stale) value the caller wrote. Two interleaved writers to one id otherwise let an
        // older writer's stale add/remove diverge the index from live state; the dangerous direction — a live candidate
        // left OUT of the index — is never re-added by the reconcile loop, so it strands the candidate. Reflection
        // drives the private seam directly because the interleaving is not reproducible through the public API.
        var (provider, _) = _Create();
        var providerType = typeof(JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>);
        var timeJobs =
            (ConcurrentDictionary<Guid, FakeTimeJob>)
                providerType.GetField("_timeJobs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(provider)!;
        var candidates =
            (ConcurrentDictionary<Guid, byte>)
                providerType
                    .GetField("_reconcileCandidates", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(provider)!;
        var sync = providerType.GetMethod("_SyncReconcileCandidate", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var id = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        FakeTimeJob Candidate(JobStatus status) =>
            new()
            {
                Id = id,
                Function = "child",
                Status = status,
                ParentId = parentId,
                RunCondition = RunCondition.OnSuccess,
                ExecutionTime = _Now,
            };

        // Live row is a candidate (Idle); a stale writer syncs a non-candidate (Succeeded) value. Convergence on live
        // means the id is ADDED, not dropped — the false-negative the fix prevents.
        timeJobs[id] = Candidate(JobStatus.Idle);
        sync.Invoke(provider, [Candidate(JobStatus.Succeeded)]);
        candidates.ContainsKey(id).Should().BeTrue("the live row is a candidate, so the index must include it");

        // Live row is now a non-candidate (Succeeded); a stale writer syncs a candidate value. Convergence on live
        // means the id is REMOVED, not re-added on a stale value.
        timeJobs[id] = Candidate(JobStatus.Succeeded);
        sync.Invoke(provider, [Candidate(JobStatus.Idle)]);
        candidates.ContainsKey(id).Should().BeFalse("the live row is not a candidate, so the index must exclude it");
    }
}
