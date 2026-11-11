// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Forwarding;

public sealed class ForwardingNonceCacheTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

    private long _Now => _time.GetUtcNow().ToUnixTimeSeconds();

    [Fact]
    public void should_refuse_a_nonce_when_it_was_already_used_inside_its_window()
    {
        // given
        var cache = new ForwardingNonceCache(_time);
        cache.TryUse("nonce", _Now + 60).Should().BeTrue();

        // when
        var reused = cache.TryUse("nonce", _Now + 60);

        // then
        reused.Should().BeFalse();
        cache.TryUse("other", _Now + 60).Should().BeTrue();
    }

    [Fact]
    public void should_refuse_a_new_nonce_when_the_cache_is_full_of_nonces_still_inside_their_window()
    {
        // given
        var cache = new ForwardingNonceCache(_time, capacity: 2);
        cache.TryUse("a", _Now + 60).Should().BeTrue();
        cache.TryUse("b", _Now + 60).Should().BeTrue();

        // when
        var third = cache.TryUse("c", _Now + 60);

        // then
        third.Should().BeFalse();
    }

    [Fact]
    public void should_make_room_when_the_cached_nonces_have_left_their_window()
    {
        // given
        var cache = new ForwardingNonceCache(_time, capacity: 2);
        cache.TryUse("a", _Now + 60).Should().BeTrue();
        cache.TryUse("b", _Now + 60).Should().BeTrue();

        // when
        _time.Advance(TimeSpan.FromSeconds(61));

        // then
        cache.TryUse("c", _Now + 60).Should().BeTrue();
        cache.TryUse("d", _Now + 60).Should().BeTrue();
        cache.TryUse("c", _Now + 60).Should().BeFalse();
    }
}
