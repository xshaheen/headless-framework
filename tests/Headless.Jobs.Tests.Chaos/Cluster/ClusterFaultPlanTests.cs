// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Cluster;

public sealed class ClusterFaultPlanTests : TestBase
{
    private static readonly TimeSpan _Duration = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan _Freeze = TimeSpan.FromSeconds(16);

    [Fact]
    public void should_build_the_same_fault_sequence_from_the_same_seed()
    {
        var first = ClusterFaultPlan.Create(424242, _Duration, faults: 5, nodes: 3, _Freeze);
        var replay = ClusterFaultPlan.Create(424242, _Duration, faults: 5, nodes: 3, _Freeze);

        replay.Faults.Should().Equal(first.Faults);
        replay.ToString().Should().Be(first.ToString()).And.StartWith("seed 424242: ");
    }

    [Fact]
    public void should_build_a_different_fault_sequence_from_a_different_seed()
    {
        var first = ClusterFaultPlan.Create(1, _Duration, faults: 5, nodes: 3, _Freeze);
        var other = ClusterFaultPlan.Create(2, _Duration, faults: 5, nodes: 3, _Freeze);

        other.Faults.Should().NotEqual(first.Faults);
    }

    [Fact]
    public void should_place_every_fault_in_its_own_slot_and_freeze_past_the_lease()
    {
        var plan = ClusterFaultPlan.Create(7, TimeSpan.FromMinutes(30), faults: 60, nodes: 3, _Freeze);
        var slot = TimeSpan.FromMinutes(30) / 60;

        plan.Faults.Should().HaveCount(60);
        plan.Faults.Select((fault, i) => (fault, i))
            .Should()
            .OnlyContain(pair =>
                pair.fault.At >= slot * pair.i
                && pair.fault.At + pair.fault.Length <= slot * (pair.i + 1)
                && pair.fault.Node >= 0
                && pair.fault.Node < 3
            );
        plan.Faults.Where(fault => fault.Kind == ClusterFaultKind.Freeze)
            .Should()
            .OnlyContain(f => f.Length == _Freeze);
        plan.Faults.Select(fault => fault.Kind)
            .Distinct()
            .Should()
            .HaveCount(Enum.GetValues<ClusterFaultKind>().Length);
    }

    [Fact]
    public void should_pass_a_fence_when_the_node_stopped_starting_renewing_and_running_on_time()
    {
        // given — cut at 10s, deadline bound 20s, fenced at 19s
        var fence = new PartitionFenceVerdict(
            "node-0#1",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(19)
        );
        var (specs, attempts) = _Running(("node-0#1", 5, 19.5), ("node-0#1", 6, 20.1), ("node-1#1", 25, 26));
        LeaseSample[] leases = [_Lease(21, 30), _Lease(23, 30), _Lease(25, 30)];

        // when
        var violations = fence.Violations(attempts, specs, leases);

        // then
        violations.Should().BeEmpty();
    }

    [Fact]
    public void should_flag_a_fenced_node_that_started_renewed_and_kept_running()
    {
        var fence = new PartitionFenceVerdict(
            "node-0#1",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(19)
        );
        var (specs, attempts) = _Running(("node-0#1", 5, 30), ("node-0#1", 6, 19.5), ("node-0#1", 22, 23));
        LeaseSample[] leases = [_Lease(21.5, 30), _Lease(23, 34)];

        var violations = fence.Violations(attempts, specs, leases);

        violations.Should().HaveCount(3);
        violations.Should().Contain(v => v.Contains("started on node-0#1 after its fence deadline"));
        violations.Should().Contain(v => v.Contains("were not cancelled"));
        violations.Should().Contain(v => v.Contains("renewed after its fence deadline"));
    }

    [Fact]
    public void should_flag_a_partition_that_proved_nothing()
    {
        var fence = new PartitionFenceVerdict("node-0#1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), null);

        var violations = fence.Violations([], new Dictionary<Guid, JobSpec>(), []);

        violations.Should().Contain(v => v.StartsWith("precondition", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Contains("never lost its membership"));
    }

    private static readonly Guid _LeasedRow = Guid.NewGuid();

    private static LeaseSample _Lease(double at, double expiresAt) =>
        new(
            TimeSpan.FromSeconds(at),
            _LeasedRow,
            3,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(expiresAt)
        );

    private static (Dictionary<Guid, JobSpec> Specs, Attempt[] Attempts) _Running(
        params (string Runner, double Start, double End)[] runs
    )
    {
        var specs = new Dictionary<Guid, JobSpec>();
        var attempts = new List<Attempt>();

        foreach (var (runner, start, end) in runs)
        {
            var spec = new JobSpec(
                Guid.NewGuid(),
                specs.Count,
                NodeDeathPolicy.Skip,
                0,
                TimeSpan.FromSeconds(30),
                false
            );
            specs[spec.Id] = spec;
            var node = runner[..runner.IndexOf('#', StringComparison.Ordinal)];
            attempts.Add(
                new Attempt(
                    spec.Id,
                    node,
                    1,
                    "c/a@1",
                    0,
                    TimeSpan.FromSeconds(start),
                    AttemptEnd.MembershipLost,
                    TimeSpan.FromSeconds(end),
                    null,
                    null
                )
            );
        }

        return (specs, [.. attempts]);
    }
}
