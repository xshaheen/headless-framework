// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CacheEntryStampsJitterTests : TestBase
{
    private static readonly DateTime _Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void should_not_jitter_when_jitter_max_is_zero()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10) };

        // when
        var stamps = CacheEntryStamps.Compute(options, _Now);

        // then — default (Zero) jitter is deterministic, exactly today's behavior
        stamps.LogicalExpiresAt.Should().Be(_Now.AddMinutes(10));
        stamps.PhysicalExpiresAt.Should().Be(_Now.AddMinutes(10));
    }

    [Fact]
    public void should_keep_logical_within_jitter_bounds_and_vary_across_writes()
    {
        // given
        var duration = TimeSpan.FromMinutes(10);
        var jitter = TimeSpan.FromMinutes(5);
        var options = new CacheEntryOptions { Duration = duration, JitterMaxDuration = jitter };
        var firstLogical = CacheEntryStamps.Compute(options, _Now).LogicalExpiresAt;
        var sawVariation = false;

        // when & then — logical lands in [now+Duration, now+Duration+Jitter) and physical never precedes logical
        for (var i = 0; i < 200; i++)
        {
            var stamps = CacheEntryStamps.Compute(options, _Now);

            stamps.LogicalExpiresAt.Should().BeOnOrAfter(_Now.Add(duration));
            stamps.LogicalExpiresAt.Should().BeBefore(_Now.Add(duration + jitter));
            stamps.PhysicalExpiresAt.Should().BeOnOrAfter(stamps.LogicalExpiresAt);

            if (stamps.LogicalExpiresAt != firstLogical)
            {
                sawVariation = true;
            }
        }

        sawVariation.Should().BeTrue("jitter should desynchronize the logical expiry across writes");
    }

    [Fact]
    public void should_flow_jitter_into_physical_retention_for_fail_safe_entry()
    {
        // given — Duration exceeds FailSafeMaxDuration, so the jittered effective duration drives physical too
        var duration = TimeSpan.FromHours(2);
        var jitter = TimeSpan.FromMinutes(30);
        var options = new CacheEntryOptions
        {
            Duration = duration,
            JitterMaxDuration = jitter,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromHours(1),
        };

        // when & then — the physical>=logical invariant holds and physical carries the same jitter as logical
        for (var i = 0; i < 200; i++)
        {
            var stamps = CacheEntryStamps.Compute(options, _Now);

            stamps.PhysicalExpiresAt.Should().Be(stamps.LogicalExpiresAt);
            stamps.PhysicalExpiresAt.Should().BeOnOrAfter(_Now.Add(duration));
            stamps.PhysicalExpiresAt.Should().BeBefore(_Now.Add(duration + jitter));
        }
    }

    [Fact]
    public void should_apply_jitter_to_eager_refresh_point()
    {
        // given
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            JitterMaxDuration = TimeSpan.FromMinutes(10),
            EagerRefreshThreshold = 0.5f,
        };

        // when & then — eager point is a fraction of the SAME jittered duration, so it stays before logical
        for (var i = 0; i < 200; i++)
        {
            var stamps = CacheEntryStamps.Compute(options, _Now);

            stamps.EagerRefreshAt.Should().NotBeNull();
            stamps.EagerRefreshAt!.Value.Should().BeOnOrAfter(_Now);
            stamps.EagerRefreshAt.Value.Should().BeBefore(stamps.LogicalExpiresAt);
        }
    }
}
