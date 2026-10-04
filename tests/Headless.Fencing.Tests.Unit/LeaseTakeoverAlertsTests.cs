// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class LeaseTakeoverAlertsTests : TestBase
{
    private static readonly LeaseKey _Key = new("", "job", "order-1");
    private static readonly FencedLease _Lease = new(null, "job", "order-1", 9);

    [Fact]
    public async Task should_warn_when_a_takeover_grant_reaches_the_threshold()
    {
        // given
        var context = new FencingTestContext();
        context.Options.TakeoverWarningThreshold = 3;
        context
            .Store.GrantAsync(_Key, FencingTestContext.Duration, AbortToken)
            .Returns(
                LeaseGrantResult.Takeover(_Lease, DateTimeOffset.UnixEpoch, previousGeneration: 8, takeoverCount: 3)
            );

        // when
        await context.Leases.GrantAsync("job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        var entry = context.Logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Name.Should().Be("FencedLeaseTakeoverThresholdReached");
        entry.Message.Should().Contain("job/order-1").And.Contain(" 3 ").And.Contain("generation 8 lost it to 9");
    }

    [Fact]
    public async Task should_warn_for_an_enlisted_takeover_grant_too()
    {
        // given
        var context = new FencingTestContext();
        context.Options.TakeoverWarningThreshold = 1;
        var (unit, _) = FencingTestContext.ActiveUnit();
        context
            .Store.GrantEnlistedAsync(unit, _Key, FencingTestContext.Duration, AbortToken)
            .Returns(
                LeaseGrantResult.Takeover(_Lease, DateTimeOffset.UnixEpoch, previousGeneration: 8, takeoverCount: 1)
            );

        // when
        await context.Feature.GrantAsync(unit, "job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        context.Logger.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task should_stay_silent_below_the_threshold_or_when_it_is_off()
    {
        // given
        var context = new FencingTestContext();
        context
            .Store.GrantAsync(_Key, FencingTestContext.Duration, AbortToken)
            .Returns(
                LeaseGrantResult.Takeover(_Lease, DateTimeOffset.UnixEpoch, previousGeneration: 8, takeoverCount: 2)
            );

        // when
        await context.Leases.GrantAsync("job", "order-1", FencingTestContext.Duration, AbortToken);
        context.Options.TakeoverWarningThreshold = 3;
        await context.Leases.GrantAsync("job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        context.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_warn_for_a_grant_that_took_nothing_over()
    {
        // given — a grant over an abandoned lease repeats the count its sweep already reported
        var context = new FencingTestContext();
        context.Options.TakeoverWarningThreshold = 1;
        context
            .Store.GrantAsync(_Key, FencingTestContext.Duration, AbortToken)
            .Returns(
                LeaseGrantResult.Granted(_Lease, DateTimeOffset.UnixEpoch, takeoverCount: 4),
                LeaseGrantResult.Held(9, DateTimeOffset.UnixEpoch, takeoverCount: 4)
            );

        // when
        await context.Leases.GrantAsync("job", "order-1", FencingTestContext.Duration, AbortToken);
        await context.Leases.GrantAsync("job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        context.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void should_carry_the_takeover_count_and_progress_on_every_grant_result()
    {
        var progress = new LeaseProgress([1], "exports.cursor/v1");

        var granted = LeaseGrantResult.Granted(_Lease, DateTimeOffset.UnixEpoch, 2, progress);
        var takeover = LeaseGrantResult.Takeover(_Lease, DateTimeOffset.UnixEpoch, 8, 3, progress);
        var held = LeaseGrantResult.Held(9, DateTimeOffset.UnixEpoch, 4);

        granted.TakeoverCount.Should().Be(2);
        granted.Progress.Should().BeSameAs(progress);
        takeover.TakeoverCount.Should().Be(3);
        takeover.Progress.Should().BeSameAs(progress);
        held.TakeoverCount.Should().Be(4);
        held.Progress.Should().BeNull();
        LeaseGrantResult.Granted(_Lease, DateTimeOffset.UnixEpoch).TakeoverCount.Should().Be(0);
    }

    [Fact]
    public void should_refuse_an_impossible_takeover_count()
    {
        var negativeGrant = () => LeaseGrantResult.Granted(_Lease, DateTimeOffset.UnixEpoch, takeoverCount: -1);
        var zeroTakeover = () =>
            LeaseGrantResult.Takeover(_Lease, DateTimeOffset.UnixEpoch, previousGeneration: 8, takeoverCount: 0);
        var negativeHeld = () => LeaseGrantResult.Held(9, DateTimeOffset.UnixEpoch, takeoverCount: -1);

        negativeGrant.Should().Throw<ArgumentOutOfRangeException>();
        zeroTakeover.Should().Throw<ArgumentOutOfRangeException>();
        negativeHeld.Should().Throw<ArgumentOutOfRangeException>();
    }
}
