// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Scenarios;

namespace Tests;

public sealed class SoakFaultPlanTests : TestBase
{
    private static readonly TimeSpan _Duration = TimeSpan.FromMinutes(2);

    [Fact]
    public void should_build_the_same_fault_sequence_from_the_same_seed()
    {
        // given
        var seed = ChaosProfile.ResolveSeed("424242");

        // when
        var first = SoakFaultPlan.Create(seed, _Duration, faults: 12, hosts: 3);
        var replay = SoakFaultPlan.Create(seed, _Duration, faults: 12, hosts: 3);

        // then
        seed.Should().Be(424242);
        replay.Faults.Should().Equal(first.Faults);
        replay.ToString().Should().Be(first.ToString()).And.StartWith("seed 424242: ");
    }

    [Fact]
    public void should_build_a_different_fault_sequence_from_a_different_seed()
    {
        var first = SoakFaultPlan.Create(1, _Duration, faults: 12, hosts: 3);
        var other = SoakFaultPlan.Create(2, _Duration, faults: 12, hosts: 3);

        other.Faults.Should().NotEqual(first.Faults);
    }

    [Fact]
    public void should_place_every_fault_inside_the_run_without_overlap()
    {
        // when
        var plan = SoakFaultPlan.Create(7, _Duration, faults: 12, hosts: 3);

        // then
        plan.Faults.Should().HaveCount(12);
        plan.Faults.Should().OnlyContain(fault => fault.At >= TimeSpan.Zero && fault.Host >= 0 && fault.Host < 3);
        plan.Faults.Zip(plan.Faults.Skip(1))
            .Should()
            .OnlyContain(
                pair => pair.First.At + pair.First.Length < pair.Second.At,
                "one fault ends before the next starts"
            );
        (plan.Faults[^1].At + plan.Faults[^1].Length).Should().BeLessThan(_Duration);
    }

    [Theory]
    [InlineData("full", "full")]
    [InlineData(" FULL ", "full")]
    [InlineData("smoke", "smoke")]
    [InlineData(null, "smoke")]
    [InlineData("nightly", "smoke")]
    public void should_pick_the_full_profile_only_when_asked(string? value, string expected)
    {
        ChaosProfile.Select(value).Name.Should().Be(expected);
    }
}
