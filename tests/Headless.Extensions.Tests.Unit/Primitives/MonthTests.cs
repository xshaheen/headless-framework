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
    public void should_return_ok_for_valid_month_when_validate(int validMonth)
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
    public void should_return_error_for_invalid_month_less_than_1_or_greater_than_12_when_validate(int invalidMonth)
    {
        // when
        var result = Month.Validate(invalidMonth);

        // then
        result.Should().NotBe(PrimitiveValidationResult.Ok);
        result.ErrorMessage.Should().Be("Month must be between 1 and 12");
        result.IsValid.Should().Be(false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void should_throw_invalid_primitive_value_exception_for_out_of_range_month_when_ctor(int invalidMonth)
    {
        // when
        var act = () => new Month(invalidMonth);

        // then
        act.Should().Throw<InvalidPrimitiveValueException>();
    }

    [Fact]
    public void should_preserve_month_value_when_json_round_trip()
    {
        // given
        var month = new Month(5);

        // when
        var json = JsonSerializer.Serialize(month);
        var deserialized = JsonSerializer.Deserialize<Month>(json);

        // then
        json.Should().Be("5");
        deserialized.Should().Be(month);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("13")]
    public void should_throw_json_exception_for_out_of_range_month_when_json_deserialize(string json)
    {
        // when - untrusted input must surface a clean JsonException, not a leaked domain exception
        var act = () => JsonSerializer.Deserialize<Month>(json);

        // then
        act.Should().Throw<JsonException>();
    }
}
