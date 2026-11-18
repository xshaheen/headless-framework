// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Cluster;

/// <summary>
/// The multi-node Jobs scenarios, each judged by <see cref="ClusterVerdict"/> on a side-effect ledger and the rows the
/// store holds afterwards: real hosts with their real scheduler, renewal, fallback, and dead-node sweeps, real faults
/// through each node's own Toxiproxy routes, and a seeded fault plan printed so a failed run replays. A leaf runs the
/// set against one store.
/// </summary>
public abstract class ClusterScenariosBase(ClusterStoreFixture store) : TestBase
{
    /// <summary>How long a scenario waits for the cluster to settle every row once the faults are over.</summary>
    private static readonly TimeSpan _Recovery = TimeSpan.FromMinutes(3);

    private static ChaosRunSettings Settings => ChaosRunSettings.Current;

    [Fact]
    public async Task should_run_every_job_within_its_policy_when_nodes_are_killed_frozen_partitioned_and_restarted()
    {
        await _SoakAsync(
            "S1 three-node loop",
            ClusterCoordination.Database,
            Settings.SoakDuration(TimeSpan.FromSeconds(150), TimeSpan.FromMinutes(30))
        );
    }

    [Fact]
    public async Task should_run_every_job_within_its_policy_when_coordination_lives_in_redis_and_faults_hit_both_stores()
    {
        await _SoakAsync(
            "S3 split stores (Redis coordination)",
            ClusterCoordination.Redis,
            Settings.SoakDuration(TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(30))
        );
    }

    [Fact]
    public async Task should_stop_starting_and_renewing_and_cancel_its_runs_when_a_node_loses_only_its_coordination_route()
    {
        // given — node-0 alone takes an opening burst of long runs, some of which ignore their token
        const string scenario = "S2 coordination-only partition";
        var seed = Settings.SeedFor(scenario);
        var output = _Output();
        await using var cluster = await JobsCluster.CreateAsync(
            store,
            ClusterCoordination.Database,
            nodes: 3,
            output,
            AbortToken
        );
        var target = cluster.Nodes[0];
        await target.StartAsync(AbortToken);
        output($"{scenario} on {store.Store}: {Settings}, scenario seed {seed}");
        var workload = new ClusterWorkload(seed);

        foreach (var ignoresToken in new[] { false, false, true })
        {
            foreach (var policy in new[] { NodeDeathPolicy.Retry, NodeDeathPolicy.Skip, NodeDeathPolicy.MarkFailed })
            {
                await cluster.EnqueueAsync(workload.Long(policy, ignoresToken), AbortToken);
            }
        }

        cluster.StartSampling();
        var runner = target.Runner;
        var ready = await cluster.WaitUntilAsync(
            () =>
            {
                var snapshot = cluster.Ledger.Snapshot();
                var running = snapshot
                    .Attempts.Where(attempt =>
                        string.Equals(attempt.Runner, runner, StringComparison.Ordinal) && attempt.StoppedAt is null
                    )
                    .ToList();
                return running.Count >= PartitionFenceVerdict.MinimumRunningAtCut
                    && running.Exists(attempt => snapshot.Specs[attempt.JobId].IgnoresToken);
            },
            TimeSpan.FromSeconds(60),
            AbortToken
        );
        await cluster.StartNodesAsync(AbortToken);
        var owner = target.OwnerTag ?? throw new InvalidOperationException($"{runner} has no owner tag.");
        cluster.WatchLeasesOf(owner);

        // when — only the coordination route goes; the Jobs store stays reachable, and the cut outlasts every body
        // the node holds, so a late completion meets a row its peers already recovered
        var cutAt = cluster.Ledger.Now;
        await target.CutAsync(coordinationOnly: true, AbortToken);
        output(
            $"{cutAt.TotalSeconds:F1}s cut the coordination route of {runner} ({owner}); opening burst ready: {ready}"
        );
        await cluster.RunWorkloadAsync(
            workload,
            perSecond: 1,
            Settings.Pick(TimeSpan.FromSeconds(70), TimeSpan.FromMinutes(5)),
            AbortToken
        );
        var recovered = await cluster.WaitForRecoveryAsync(_Recovery, AbortToken);
        await cluster.StopSamplingAsync();

        // then
        var fence = new PartitionFenceVerdict(
            runner,
            cutAt,
            cutAt + ClusterTimings.DeadThreshold - ClusterTimings.SelfFenceMargin,
            target.MembershipLostAt
        );
        var judgement = await cluster.JudgeAsync(AbortToken);
        List<string> violations =
        [
            .. judgement.Verdict.Violations,
            .. fence.Violations(judgement.Ledger.Attempts, judgement.Ledger.Specs, cluster.LeaseSamples()),
        ];
        _AddIf(violations, !recovered, $"the cluster did not settle within {_Recovery}");

        _Report(
            scenario,
            violations,
            $"{fence}; {judgement.Verdict.Counts}; {_Health(cluster)}",
            judgement.Evidence(cluster.Log),
            output
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_flag_a_skip_job_when_its_body_runs_its_side_effect_twice()
    {
        // given — a negative control: one Skip body records its side effect twice on purpose
        const string scenario = "negative control: Skip side effect twice";
        var output = _Output();
        await using var cluster = await JobsCluster.CreateAsync(
            store,
            ClusterCoordination.Database,
            nodes: 2,
            output,
            AbortToken
        );
        await cluster.StartNodesAsync(AbortToken);
        var workload = new ClusterWorkload(Settings.SeedFor(scenario));
        var sabotaged = await cluster.EnqueueAsync(
            new WorkItem(-1, NodeDeathPolicy.Skip, 0, TimeSpan.FromMilliseconds(300), IgnoresToken: false),
            AbortToken,
            doubleRun: true
        );

        for (var i = 0; i < 10; i++)
        {
            await cluster.EnqueueAsync(
                workload.Next() with
                {
                    Work = TimeSpan.FromMilliseconds(200),
                    IgnoresToken = false,
                },
                AbortToken
            );
        }

        // when
        var recovered = await cluster.WaitForRecoveryAsync(_Recovery, AbortToken);
        var judgement = await cluster.JudgeAsync(AbortToken);

        // then — the oracle names the sabotaged job, and nothing else
        var id = sabotaged?.Id.ToString() ?? "(not enqueued)";
        var caught = judgement.Verdict.Violations.Any(violation =>
            violation.Contains(id, StringComparison.Ordinal)
            && violation.Contains("started 2x", StringComparison.Ordinal)
        );
        List<string> violations = [];
        _AddIf(violations, sabotaged is null, "the sabotaged job was not enqueued");
        _AddIf(violations, !recovered, $"the cluster did not settle within {_Recovery}");
        _AddIf(violations, !caught, $"the oracle missed Skip job {id}, whose body ran its side effect twice");
        _AddIf(
            violations,
            judgement.Verdict.ViolatingJobs.Any(job => !string.Equals(job.ToString(), id, StringComparison.Ordinal)),
            $"the oracle flagged jobs other than {id}: {string.Join(", ", judgement.Verdict.ViolatingJobs)}"
        );

        _Report(
            scenario,
            violations,
            $"oracle said: {string.Join(" | ", judgement.Verdict.Violations)}; {judgement.Verdict.Counts}",
            judgement.Evidence(cluster.Log),
            output
        );
        violations.Should().BeEmpty();
        judgement.Verdict.Violations.Should().ContainSingle();
    }

    [Fact]
    public async Task should_flag_an_orphan_when_a_row_stays_owned_by_an_owner_no_sweep_recovers()
    {
        // given — a negative control: a running row stamped by another cluster's dead node, with a lease that outlasts
        // the run, which neither the stalled reclaim nor this cluster's orphan sweep may touch
        const string scenario = "negative control: orphaned row";
        var output = _Output();
        await using var cluster = await JobsCluster.CreateAsync(
            store,
            ClusterCoordination.Database,
            nodes: 1,
            output,
            AbortToken
        );
        await cluster.StartNodesAsync(AbortToken);
        var orphan = Guid.NewGuid();
        await cluster.Reader.View.SeedTimeJobAsync(
            orphan,
            ClusterJobsModule.FunctionName,
            (int)JobStatus.InProgress,
            "foreign-cluster/ghost@1",
            AbortToken,
            NodeDeathPolicy.Skip,
            // wall-clock: the seeded lease must outlast the run on the store's clock, which tracks this machine's
            lockedUntil: DateTime.UtcNow.AddMinutes(10)
        );

        // when
        cluster.StartSampling();
        await cluster.WaitUntilAsync(
            () =>
                cluster
                    .Tracker.Intervals()
                    .Any(interval => interval.Key.RowId == orphan && interval.Duration > ClusterTimings.OrphanBound),
            ClusterTimings.OrphanBound * 3,
            AbortToken
        );
        await cluster.StopSamplingAsync();
        var judgement = await cluster.JudgeAsync(AbortToken);

        // then
        var expected = $"orphan on row {orphan}";
        var caught = judgement.Verdict.Violations.Any(violation =>
            violation.Contains(expected, StringComparison.Ordinal)
        );
        List<string> violations = [];
        _AddIf(violations, !caught, $"the oracle missed the {expected}");

        _Report(
            scenario,
            violations,
            $"samples {cluster.Tracker.Samples}; oracle said: {string.Join(" | ", judgement.Verdict.Violations)}",
            judgement.Evidence(cluster.Log),
            output
        );
        violations.Should().BeEmpty();
    }

    private async Task _SoakAsync(string scenario, ClusterCoordination coordination, TimeSpan soak)
    {
        // given
        var seed = Settings.SeedFor(scenario);
        var output = _Output();
        await using var cluster = await JobsCluster.CreateAsync(store, coordination, nodes: 3, output, AbortToken);
        await cluster.StartNodesAsync(AbortToken);
        var plan = ClusterFaultPlan.Create(
            seed,
            soak,
            faults: Math.Max(3, (int)(soak.TotalSeconds / 30)),
            nodes: cluster.Nodes.Count,
            ClusterTimings.FreezeLength
        );
        output($"{scenario} on {store.Store}: {Settings}, scenario seed {seed}, soak {soak}; fault plan {plan}");

        // when — jobs keep arriving while the plan kills, freezes, partitions, and restarts nodes
        cluster.StartSampling();
        var origin = cluster.Ledger.Now;
        var enqueue = cluster.RunWorkloadAsync(new ClusterWorkload(seed), perSecond: 2, soak, AbortToken);
        var faults = cluster.RunFaultPlanAsync(plan, origin, AbortToken);
        await Task.WhenAll(enqueue, faults);
        var faultsEndedAt = cluster.Ledger.Now;
        var recovered = await cluster.WaitForRecoveryAsync(_Recovery, AbortToken);
        await cluster.StopSamplingAsync();

        // then
        var judgement = await cluster.JudgeAsync(AbortToken);
        var injected = await faults;
        var enqueued = await enqueue;
        List<string> violations = [.. judgement.Verdict.Violations];
        _AddIf(violations, !recovered, $"the cluster did not settle within {_Recovery} of the last fault");

        _Report(
            scenario,
            violations,
            string.Create(
                CultureInfo.InvariantCulture,
                $"faults {injected}, enqueue attempts {enqueued}; {judgement.Verdict.Counts}; recovered {(cluster.Ledger.Now - faultsEndedAt).TotalSeconds:F0}s after the last fault; {_Health(cluster)}"
            ),
            judgement.Evidence(cluster.Log),
            output
        );
        violations.Should().BeEmpty();
    }

    private static string _Health(JobsCluster cluster) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"wall {cluster.Ledger.Now.TotalSeconds:F0}s, enqueue failures {cluster.CountEnqueueFailures()}, sample failures {cluster.CountSampleFailures()}/{cluster.Tracker.Samples}, observation failures {cluster.Reader.CountObservationFailures()}, node warnings {cluster.Log.Count}"
        );

    private void _Report(
        string scenario,
        List<string> violations,
        string counts,
        string evidence,
        Action<string> output
    ) => ChaosReport.Record(store.Store, scenario, violations, counts, evidence, output);

    private static Action<string> _Output()
    {
        var helper = TestContext.Current.TestOutputHelper;

        return line =>
        {
            try
            {
                helper?.WriteLine(line);
            }
            catch (InvalidOperationException)
            {
                // The test already finished; a late line from its teardown has nowhere to go.
            }
        };
    }

    private static void _AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }
}
