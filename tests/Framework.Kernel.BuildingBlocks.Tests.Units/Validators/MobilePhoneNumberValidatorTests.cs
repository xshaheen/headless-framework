// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.BuildingBlocks.Validators;

namespace Tests.Validators;

public sealed class MobilePhoneNumberValidatorTests
{
    [Theory]
    [InlineData("1234567890", 1, true)]
    [InlineData("1234567890", 44, true)]
    [InlineData("", 1, false)]
    [InlineData(null, 1, false)]
    public void should_returns_expected_result_when_is_valid_with_country_code(
        string? phoneNumber,
        int countryCode,
        bool expected
    )
    {
        // given, when
        var result = MobilePhoneNumberValidator.IsValid(phoneNumber!, countryCode);

        // then
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1234567890", "US", true)]
    [InlineData("1234567890", "GB", true)]
    [InlineData("", "US", false)]
    [InlineData(null, "US", false)]
    public void should_returns_expected_result_when_is_valid_with_region_code(
        string? phoneNumber,
        string regionCode,
        bool expected
    )
    {
        // given, when
        var result = MobilePhoneNumberValidator.IsValid(phoneNumber!, regionCode);

        // then
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("+11234 5678 90", true, "+11234567890")]
    [InlineData("+441 234567 890", true, "+441234567890")]
    [InlineData("+441234567890", true, "+441234567890")]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void should_returns_expected_result_when_is_valid_with_international_phone_number(
        string? internationalPhoneNumber,
        bool expected,
        string? expectedNormalized
    )
    {
        // given, when
        var result = MobilePhoneNumberValidator.IsValid(internationalPhoneNumber, out var normalizedPhoneNumber);

        // then
        result.Should().Be(expected);
        normalizedPhoneNumber.Should().Be(expectedNormalized);
    }
}
