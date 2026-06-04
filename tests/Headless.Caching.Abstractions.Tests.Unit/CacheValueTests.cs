// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

public sealed class CacheValueTests
{
    [Fact]
    public void should_default_to_not_stale()
    {
        // when
        var value = new CacheValue<string>("value", hasValue: true);

        // then
        value.HasValue.Should().BeTrue();
        value.IsStale.Should().BeFalse();
    }

    [Fact]
    public void should_mark_value_as_stale_when_requested()
    {
        // when
        var value = new CacheValue<string>("value", hasValue: true, isStale: true);

        // then
        value.HasValue.Should().BeTrue();
        value.IsStale.Should().BeTrue();
    }

    [Fact]
    public void should_keep_static_values_not_stale()
    {
        // then
        CacheValue<string>.Null.IsStale.Should().BeFalse();
        CacheValue<string>.NoValue.IsStale.Should().BeFalse();
    }
}
