// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>The sliding re-arm throttle: a deadline move counts only when it exceeds <c>min(1 s, 10% of the window)</c>.</summary>
public sealed class CacheEntryStampsSlidingRearmTests : TestBase
{
    private static readonly DateTime _Deadline = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(200, 20)]
    [InlineData(2_000, 200)]
    [InlineData(10_000, 1_000)]
    [InlineData(600_000, 1_000)]
    public void should_use_a_tenth_of_the_window_capped_at_one_second(int slidingMs, int expectedToleranceMs)
    {
        var tolerance = CacheEntryStamps.GetSlidingRearmTolerance(TimeSpan.FromMilliseconds(slidingMs));

        tolerance.Should().Be(TimeSpan.FromMilliseconds(expectedToleranceMs));
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(600_000)]
    public void should_rearm_only_when_the_deadline_moves_by_more_than_the_tolerance(int slidingMs)
    {
        var sliding = TimeSpan.FromMilliseconds(slidingMs);
        var tolerance = CacheEntryStamps.GetSlidingRearmTolerance(sliding);

        CacheEntryStamps.ShouldRearmSliding(_Deadline, _Deadline.Add(tolerance), sliding).Should().BeFalse();
        CacheEntryStamps.ShouldRearmSliding(_Deadline, _Deadline.Add(tolerance).AddTicks(1), sliding).Should().BeTrue();
    }

    [Fact]
    public void should_never_rearm_a_move_that_shortens_the_deadline()
    {
        var sliding = TimeSpan.FromSeconds(10);

        CacheEntryStamps.ShouldRearmSliding(_Deadline, _Deadline.AddSeconds(-5), sliding).Should().BeFalse();
    }
}
