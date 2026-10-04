// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;
using Headless.Testing.Tests;

namespace Tests.Persistence;

public sealed class RetryDelayTests : TestBase
{
    private static readonly DateTimeOffset _StoreNow = new(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void should_fall_due_after_delay_when_exactly_replaces_a_later_due_time()
    {
        var delay = RetryDelay.Exactly(TimeSpan.FromSeconds(5));

        delay.KeepsLaterDue.Should().BeFalse();
        delay.ResolveDueAt(_StoreNow, currentDue: _StoreNow.AddMinutes(10)).Should().Be(_StoreNow.AddSeconds(5));
    }

    [Fact]
    public void should_keep_later_due_time_when_at_least()
    {
        var delay = RetryDelay.AtLeast(TimeSpan.FromSeconds(5));
        var later = _StoreNow.AddMinutes(10);

        delay.KeepsLaterDue.Should().BeTrue();
        delay.ResolveDueAt(_StoreNow, later).Should().Be(later);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-60)]
    [InlineData(5)]
    public void should_fall_due_after_delay_when_at_least_has_no_later_due_time(int? currentDueOffsetSeconds)
    {
        var delay = RetryDelay.AtLeast(TimeSpan.FromSeconds(5));
        DateTimeOffset? currentDue = currentDueOffsetSeconds is { } seconds ? _StoreNow.AddSeconds(seconds) : null;

        delay.ResolveDueAt(_StoreNow, currentDue).Should().Be(_StoreNow.AddSeconds(5));
    }

    [Fact]
    public void should_throw_when_delay_is_negative()
    {
        var exactly = () => RetryDelay.Exactly(TimeSpan.FromTicks(-1));
        var atLeast = () => RetryDelay.AtLeast(TimeSpan.FromTicks(-1));

        exactly.Should().Throw<ArgumentOutOfRangeException>();
        atLeast.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_accept_zero_delay()
    {
        RetryDelay.Exactly(TimeSpan.Zero).ResolveDueAt(_StoreNow, currentDue: null).Should().Be(_StoreNow);
    }
}
