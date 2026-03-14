// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;
using Month = Headless.Primitives.Month;

namespace Tests.Primitives;

public sealed class MonthTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(5)]
    [InlineData(10)]
    public void validate_should_return_ok_for_valid_month(int validMonth)
    {
        // when
        var result = Month.Validate(validMonth);

        // then
        result.Should().Be(PrimitiveValidationResult.Ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(13)]
    [InlineData(100)]
    public void validate_should_return_error_for_invalid_month_less_than_1_or_greater_than_12(int invalidMonth)
    {
        // when
        var result = Month.Validate(invalidMonth);

        // then
        result.Should().NotBe(PrimitiveValidationResult.Ok);
        result.ErrorMessage.Should().Be("Month must be between 1 and 12");
        result.IsValid.Should().Be(false);
    }
}
