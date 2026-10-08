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
}
