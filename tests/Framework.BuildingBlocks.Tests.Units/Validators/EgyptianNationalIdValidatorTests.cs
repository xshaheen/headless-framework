// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Validators;

namespace Tests.Validators;

public sealed class EgyptianNationalIdValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("1234567890123", false)] // Less than 14 digits
    [InlineData("123456789012345", false)] // More than 14 digits
    [InlineData("1234567890123a", false)] // Contains non-digit character
    [InlineData("00000000000000", false)] // Invalid date
    [InlineData("30000000000000", false)] // Invalid century indicator
    [InlineData("29912319945678", false)] // Invalid governorate code
    [InlineData("29901010123456", true)] // Valid national ID
    public void IsValid_ShouldReturnExpectedResult(string? nationalId, bool expected)
    {
        // given, when
        var result = EgyptianNationalIdValidator.IsValid(nationalId!);

        // then
        result.Should().Be(expected);
    }
}
