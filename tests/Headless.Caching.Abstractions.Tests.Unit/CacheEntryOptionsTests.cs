// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

public sealed class CacheEntryOptionsTests
{
    [Fact]
    public void should_create_options_from_timespan()
    {
        // given
        var duration = TimeSpan.FromMinutes(5);

        // when
        CacheEntryOptions options = duration;

        // then
        options.Duration.Should().Be(duration);
        options.SlidingExpiration.Should().BeNull();
        options.IsFailSafeEnabled.Should().BeFalse();
        options.FailSafeMaxDuration.Should().Be(CacheEntryOptions.DefaultFailSafeMaxDuration);
        options.FailSafeThrottleDuration.Should().Be(CacheEntryOptions.DefaultFailSafeThrottleDuration);
    }

    [Fact]
    public void should_round_trip_explicit_duration()
    {
        // given
        var duration = TimeSpan.FromSeconds(30);

        // when
        var options = new CacheEntryOptions { Duration = duration };

        // then
        options.Duration.Should().Be(duration);
    }

    [Fact]
    public void should_accept_timespan_where_cache_entry_options_is_expected()
    {
        // given
        var duration = TimeSpan.FromHours(1);

        // when
        var options = _CreateOptions(duration);

        // then
        options.Duration.Should().Be(duration);
    }

    [Fact]
    public void should_use_failsafe_defaults()
    {
        // when
        var options = new CacheEntryOptions();

        // then
        options.IsFailSafeEnabled.Should().BeFalse();
        options.FailSafeMaxDuration.Should().Be(TimeSpan.FromDays(1));
        options.FailSafeThrottleDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public void should_round_trip_failsafe_options()
    {
        // given
        var duration = TimeSpan.FromSeconds(15);
        var maxDuration = TimeSpan.FromMinutes(10);
        var throttleDuration = TimeSpan.FromSeconds(3);

        // when
        var options = new CacheEntryOptions
        {
            Duration = duration,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = maxDuration,
            FailSafeThrottleDuration = throttleDuration,
        };

        // then
        options.Duration.Should().Be(duration);
        options.IsFailSafeEnabled.Should().BeTrue();
        options.FailSafeMaxDuration.Should().Be(maxDuration);
        options.FailSafeThrottleDuration.Should().Be(throttleDuration);
    }

    [Fact]
    public void should_round_trip_sliding_expiration()
    {
        // given
        var slidingExpiration = TimeSpan.FromMinutes(2);

        // when
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            SlidingExpiration = slidingExpiration,
        };

        // then
        options.SlidingExpiration.Should().Be(slidingExpiration);
    }

    private static CacheEntryOptions _CreateOptions(CacheEntryOptions options)
    {
        return options;
    }
}
