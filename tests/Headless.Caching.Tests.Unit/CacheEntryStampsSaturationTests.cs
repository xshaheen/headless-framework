// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

/// <summary><see cref="TimeSpan.MaxValue"/> is the "never expire" duration, so the stamps saturate instead of throwing.</summary>
public sealed class CacheEntryStampsSaturationTests : TestBase
{
    private static readonly DateTime _Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void should_saturate_at_the_max_date_when_duration_is_max_value()
    {
        var options = new CacheEntryOptions { Duration = TimeSpan.MaxValue };

        var stamps = CacheEntryStamps.Compute(options, _Now);

        stamps.LogicalExpiresAt.Should().Be(DateTime.MaxValue);
        stamps.PhysicalExpiresAt.Should().Be(DateTime.MaxValue);
    }

    [Fact]
    public void should_saturate_when_jitter_is_added_to_a_max_value_duration()
    {
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.MaxValue,
            JitterMaxDuration = TimeSpan.FromMinutes(5),
        };

        var stamps = CacheEntryStamps.Compute(options, _Now);

        stamps.LogicalExpiresAt.Should().Be(DateTime.MaxValue);
        stamps.PhysicalExpiresAt.Should().Be(DateTime.MaxValue);
    }

    [Fact]
    public void should_saturate_fail_safe_and_eager_stamps_for_a_max_value_duration()
    {
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.MaxValue,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.MaxValue,
            EagerRefreshThreshold = 0.9f,
        };

        var stamps = CacheEntryStamps.Compute(options, _Now);

        stamps.PhysicalExpiresAt.Should().Be(DateTime.MaxValue);
        stamps.EagerRefreshAt.Should().Be(DateTime.MaxValue);
    }

    [Fact]
    public void should_saturate_the_physical_stamp_of_a_sliding_entry_and_keep_the_sliding_window()
    {
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.MaxValue,
            SlidingExpiration = TimeSpan.FromMinutes(1),
        };

        var stamps = CacheEntryStamps.Compute(options, _Now);

        stamps.PhysicalExpiresAt.Should().Be(DateTime.MaxValue);
        stamps.LogicalExpiresAt.Should().Be(_Now.AddMinutes(1));
    }
}
