// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;

namespace Tests.Core;

public sealed class FixedTimezoneTimeProviderTests
{
    public static readonly TheoryData<string> SystemTimeZoneIds =
    [
        "China Standard Time",
        "America/Los_Angeles",
        "Central European Standard Time",
        "Pacific Standard Time",
    ];

    [Theory]
    [MemberData(nameof(SystemTimeZoneIds))]
    public void local_timeZone_should_return_configured_time_zone(string id)
    {
        // given
        var expectedTimeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        var timeProvider = new FixedTimezoneTimeProvider(expectedTimeZone);

        // when
        var actualTimeZone = timeProvider.LocalTimeZone;

        // then
        actualTimeZone.Should().Be(expectedTimeZone);
    }
}
