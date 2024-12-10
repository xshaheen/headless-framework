// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;

namespace Tests.Abstractions;

public sealed class ClockTests
{
    private readonly TimeProvider _sutTimeProvider;
    private readonly IClock _clock;

    public ClockTests()
    {
        _sutTimeProvider = Substitute.For<TimeProvider>();
        _sutTimeProvider.GetUtcNow().Returns(new DateTimeOffset(2024, 11, 27, 12, 0, 0, TimeSpan.Zero));
        _sutTimeProvider.LocalTimeZone.Returns(TimeZoneInfo.Local);
        _clock = new Clock(_sutTimeProvider);
    }

    [Fact]
    public void utc_now_should_return_correct_utc_time()
    {
        // when
        var utcNow = _clock.UtcNow;

        // then
        utcNow.Should().Be(new DateTimeOffset(2024, 11, 27, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ticks_should_return_environment_tick_count()
    {
        // when
        var ticks = _clock.Ticks;

        // then
        ticks.Should().Be(Environment.TickCount64);
    }

    [Fact]
    public void get_timestamp_should_return_correct_value()
    {
        // given
        _sutTimeProvider.GetTimestamp().Returns(123456789L);

        // when
        var timestamp = _clock.GetTimestamp();

        // then
        timestamp.Should().Be(123456789L);
    }

    [Fact]
    public void normalize_should_return_correct_date_time_based_on_kind()
    {
        // given
        var localDateTime = new DateTime(2024, 11, 27, 12, 0, 0, DateTimeKind.Local);

        // when
        var normalized = _clock.Normalize(localDateTime);

        // then
        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Should().Be(localDateTime.ToUniversalTime());
    }
}
