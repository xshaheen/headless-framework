// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;

namespace Tests.Core;

public sealed class DateTimeOffsetExtensionsTests
{
    [Fact]
    public void should_convert_to_egypt_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        // when
        var result = utc.ToEgyptTimeZone();

        // then
        result.Offset.Should().Be(TimezoneConstants.EgyptTimeZone.GetUtcOffset(utc));
        result.UtcDateTime.Should().Be(utc.UtcDateTime);
    }

    [Fact]
    public void should_convert_to_palestine_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        // when
        var result = utc.ToPalestineTimeZone();

        // then
        result.Offset.Should().Be(TimezoneConstants.GazaTimeZone.GetUtcOffset(utc));
        result.UtcDateTime.Should().Be(utc.UtcDateTime);
    }

    [Fact]
    public void should_convert_to_saudi_arabia_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        // when
        var result = utc.ToSaudiArabiaTimeZone();

        // then
        result.Offset.Should().Be(TimezoneConstants.SaudiArabiaTimeZone.GetUtcOffset(utc));
        result.UtcDateTime.Should().Be(utc.UtcDateTime);
    }

    [Fact]
    public void should_preserve_instant_when_converting_to_egypt_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);

        // when
        var result = utc.ToEgyptTimeZone();

        // then
        result.ToUniversalTime().Should().Be(utc.ToUniversalTime());
    }

    [Fact]
    public void should_preserve_instant_when_converting_to_palestine_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 7, 20, 8, 15, 30, TimeSpan.Zero);

        // when
        var result = utc.ToPalestineTimeZone();

        // then
        result.ToUniversalTime().Should().Be(utc.ToUniversalTime());
    }

    [Fact]
    public void should_preserve_instant_when_converting_to_saudi_arabia_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 3, 10, 14, 45, 0, TimeSpan.Zero);

        // when
        var result = utc.ToSaudiArabiaTimeZone();

        // then
        result.ToUniversalTime().Should().Be(utc.ToUniversalTime());
    }
}
