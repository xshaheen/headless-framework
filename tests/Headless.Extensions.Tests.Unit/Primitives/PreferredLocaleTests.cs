// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class PreferredLocaleTests
{
    [Fact]
    public void should_normalize_country_to_upper_and_language_to_lower()
    {
        // when
        var locale = new PreferredLocale(country: "usa", language: "EN");

        // then
        locale.Country.Should().Be("USA");
        locale.Language.Should().Be("en");
        locale.ToString().Should().Be("en-USA");
    }

    [Fact]
    public void should_be_case_insensitive_after_normalization_when_equality()
    {
        // given - the bug: values were stored verbatim and compared case-sensitively
        var a = new PreferredLocale(country: "USA", language: "en");
        var b = new PreferredLocale(country: "usa", language: "EN");

        // then
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void should_still_distinguish_different_locales_when_equality()
    {
        // given
        var enUs = new PreferredLocale(country: "USA", language: "en");
        var arEg = new PreferredLocale(country: "EGY", language: "ar");

        // then
        enUs.Equals(arEg).Should().BeFalse();
        (enUs != arEg).Should().BeTrue();
    }
}
