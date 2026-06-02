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

    private static CacheEntryOptions _CreateOptions(CacheEntryOptions options) => options;
}
