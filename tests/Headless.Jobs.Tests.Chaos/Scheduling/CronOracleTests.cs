// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests.Scheduling;

/// <summary>The cron oracles must catch each fault they exist for: a duplicated, a missing, and a stray run.</summary>
public sealed class CronOracleTests : TestBase
{
    private static readonly DateTime _T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan _Cadence = TimeSpan.FromSeconds(5);

    [Fact]
    public void should_pass_when_every_owed_instant_fired_once_on_any_node()
    {
        var owed = _Owed(6);
        var fires = owed.Select((x, i) => new CronFire("f", x, $"node-{i % 3}")).ToList();

        var verdict = CronInstantVerdict.Evaluate(owed, _OnSchedule, fires);

        verdict.Violations().Should().BeEmpty();
    }

    [Fact]
    public void should_catch_a_duplicated_fire_when_two_nodes_run_one_instant()
    {
        var owed = _Owed(6);
        List<CronFire> fires = [.. owed.Select(x => new CronFire("f", x, "node-0")), new("f", owed[2], "node-1")];

        var verdict = CronInstantVerdict.Evaluate(owed, _OnSchedule, fires);

        verdict.Duplicated.Should().Equal(owed[2]);
        verdict.Violations().Should().ContainSingle().Which.Should().Contain("more than once");
    }

    [Fact]
    public void should_catch_a_missing_fire_when_no_recovery_run_covers_it()
    {
        var owed = _Owed(6);
        var fires = owed.Where(x => x != owed[3]).Select(x => new CronFire("f", x, "node-0")).ToList();

        var verdict = CronInstantVerdict.Evaluate(owed, _OnSchedule, fires);

        verdict.Missing.Should().Equal(owed[3]);
        verdict.Violations().Should().ContainSingle().Which.Should().Contain("never fired");
    }

    [Fact]
    public void should_excuse_instants_a_coalesced_recovery_run_stands_in_for()
    {
        // The recovery run reports the first missed instant (2); instants 3 and 4 belong to its backlog.
        var owed = _Owed(6);
        List<CronFire> fires =
        [
            new("f", owed[0], "node-0"),
            new("f", owed[1], "node-0"),
            new("f", owed[2], "node-1", RecoveredFromUtc: owed[2]),
            new("f", owed[5], "node-0"),
        ];

        var verdict = CronInstantVerdict.Evaluate(owed, _OnSchedule, fires);

        verdict.Excused.Should().Equal(owed[3], owed[4]);
        verdict.Violations().Should().BeEmpty();
    }

    [Fact]
    public void should_catch_instants_after_a_recovery_run_when_the_schedule_never_fires_again()
    {
        // A recovery run at instant 1 and then silence: instants 2..4 are a stopped schedule, not a coalesced backlog.
        var owed = _Owed(5);
        List<CronFire> fires = [new("f", owed[0], "node-0"), new("f", owed[1], "node-1", RecoveredFromUtc: owed[1])];

        var verdict = CronInstantVerdict.Evaluate(owed, _OnSchedule, fires);

        verdict.Missing.Should().Equal(owed[2], owed[3], owed[4]);
        verdict.Excused.Should().BeEmpty();
        verdict.Violations().Should().ContainSingle(x => x.Contains("never fired", StringComparison.Ordinal));
    }

    [Fact]
    public void should_catch_a_fire_at_an_instant_the_schedule_does_not_place()
    {
        var owed = _Owed(3);
        List<CronFire> fires =
        [
            .. owed.Select(x => new CronFire("f", x, "node-0")),
            new("f", owed[1].AddSeconds(1), "node-2"),
        ];

        var verdict = CronInstantVerdict.Evaluate(owed, _OnSchedule, fires);

        verdict.OffSchedule.Should().Equal(owed[1].AddSeconds(1));
        verdict.Violations().Should().ContainSingle().Which.Should().Contain("does not place");
    }

    [Fact]
    public void should_fail_a_window_that_owed_nothing()
    {
        CronInstantVerdict.Evaluate([], _OnSchedule, []).Violations().Should().ContainSingle();
    }

    [Fact]
    public void should_pass_a_fire_list_equal_to_the_golden_list()
    {
        var golden = _Owed(4);

        var violations = FireListOracle.Compare("t", _Expected(golden), _Fires(golden));

        violations.Should().BeEmpty();
    }

    [Fact]
    public void should_catch_a_duplicated_fire_in_a_golden_list()
    {
        var golden = _Owed(4);

        var violations = FireListOracle.Compare("t", _Expected(golden), _Fires([.. golden, golden[1]]));

        violations.Should().ContainSingle().Which.Should().Contain("more than once");
    }

    [Fact]
    public void should_catch_a_missing_fire_in_a_golden_list()
    {
        var golden = _Owed(4);

        var violations = FireListOracle.Compare("t", _Expected(golden), _Fires([golden[0], golden[1], golden[3]]));

        violations.Should().ContainSingle().Which.Should().Contain("never fired");
    }

    [Fact]
    public void should_catch_a_second_pass_the_golden_list_does_not_hold()
    {
        var golden = _Owed(2);

        var violations = FireListOracle.Compare("t", _Expected(golden), _Fires([.. golden, golden[1].AddHours(1)]));

        violations.Should().ContainSingle().Which.Should().Contain("off the golden list");
    }

    [Fact]
    public void should_catch_a_recovery_run_when_the_clock_reached_every_instant_on_time()
    {
        var golden = _Owed(2);
        List<CronFire> fires = [new("f", golden[0], "local"), new("f", golden[1], "local", golden[1])];

        FireListOracle
            .Compare("t", _Expected(golden), fires)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("recovery");
        FireListOracle.Compare("t", _Expected(golden), fires, allowRecoveryRuns: true).Should().BeEmpty();
    }

    private static bool _OnSchedule(DateTime instant) => instant.Ticks % _Cadence.Ticks == 0;

    private static List<DateTime> _Owed(int count) => [.. Enumerable.Range(1, count).Select(i => _T0 + (_Cadence * i))];

    private static Dictionary<string, IReadOnlyList<DateTime>> _Expected(List<DateTime> golden) =>
        new(StringComparer.Ordinal) { ["f"] = golden };

    private static List<CronFire> _Fires(IEnumerable<DateTime> instants) =>
        [.. instants.Select(x => new CronFire("f", x, "local"))];
}
