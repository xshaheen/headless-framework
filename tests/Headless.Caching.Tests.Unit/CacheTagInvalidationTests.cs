// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CacheTagInvalidationTests : TestBase
{
    private static readonly DateTime _Marker = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void should_not_invalidate_when_no_marker_applies()
    {
        CacheTagInvalidation.IsInvalidated(_Marker, newestMarker: null).Should().BeFalse();
        CacheTagInvalidation.IsInvalidated(createdAt: null, newestMarker: null).Should().BeFalse();
    }

    [Fact]
    public void should_invalidate_when_the_entry_has_no_birth_time_and_a_marker_applies()
    {
        CacheTagInvalidation.IsInvalidated(createdAt: null, _Marker).Should().BeTrue();
    }

    [Fact]
    public void should_invalidate_when_the_entry_was_born_before_the_marker()
    {
        CacheTagInvalidation.IsInvalidated(_Marker.AddTicks(-1), _Marker).Should().BeTrue();
    }

    [Fact]
    public void should_not_invalidate_when_the_entry_was_born_at_the_marker_instant()
    {
        CacheTagInvalidation.IsInvalidated(_Marker, _Marker).Should().BeFalse();
    }

    [Fact]
    public void should_not_invalidate_when_the_entry_was_born_after_the_marker()
    {
        CacheTagInvalidation.IsInvalidated(_Marker.AddTicks(1), _Marker).Should().BeFalse();
    }
}
