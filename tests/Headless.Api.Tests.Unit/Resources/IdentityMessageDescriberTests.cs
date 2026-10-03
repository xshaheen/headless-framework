// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Api.Resources;
using Headless.Testing.Tests;
using Humanizer;

namespace Tests.Resources;

public sealed class IdentityMessageDescriberTests : TestBase
{
    [Fact]
    public void should_humanize_cooldown_durations_in_arabic_when_ui_culture_is_arabic()
    {
        // given: CultureInfo.CurrentUICulture flows through the async context, so this swap stays local to the test.
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var arabic = CultureInfo.GetCultureInfo("ar");
        var cooldown = TimeSpan.FromMinutes(10);
        var remaining = TimeSpan.FromMinutes(5);

        try
        {
            CultureInfo.CurrentUICulture = arabic;

            // when
            var descriptor = IdentityMessageDescriber.Passwords.RequestForgetCooldown(remaining, cooldown);

            // then: the durations come from Humanizer.Core.ar, not the English fallback
            var arabicRemaining = remaining.Humanize(culture: arabic);
            arabicRemaining.Should().NotBe(remaining.Humanize(culture: CultureInfo.InvariantCulture));
            arabicRemaining.Should().NotContainAny("minute", "Minute");
            descriptor.Description.Should().Contain(arabicRemaining);
            descriptor.Description.Should().Contain(cooldown.Humanize(culture: arabic));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
