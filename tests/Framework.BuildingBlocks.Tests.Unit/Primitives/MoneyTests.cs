// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Generator.Primitives;
using Framework.Primitives;

namespace Tests.Primitives;

public sealed class MoneyTests
{
    [Fact]
    public void money_zero_should_be_zero()
    {
        // given & when
        var result = Money.Zero;

        // then
        result.Should().Be(new Money(0));
    }

    [Fact]
    public void get_rounded_should_round_up_to_two_decimal_places()
    {
        // given
        var money = new Money(5.678m);

        // when
        var result = money.GetRounded();

        // then
        result.Should().Be(new Money(5.68m));
    }

    [Fact]
    public void validate_should_return_ok_for_valid_value()
    {
        // given
        const decimal value = 10m;

        // when
        var result = Money.Validate(value);

        // then
        result.Should().Be(PrimitiveValidationResult.Ok);
    }

    [Fact]
    public void should_round_to_positive_infinity()
    {
        // given - value at midpoint
        var money = new Money(5.125m);

        // when
        var result = money.GetRounded();

        // then - rounds toward positive infinity (up)
        result.Should().Be(new Money(5.13m));
    }

    [Fact]
    public void should_round_to_two_decimal_places()
    {
        // given
        var money = new Money(123.456789m);

        // when
        var result = money.GetRounded();

        // then
        result.Should().Be(new Money(123.46m));
    }

    [Fact]
    public void should_return_zero_static_instance()
    {
        // given & when
        var zero = Money.Zero;

        // then
        ((decimal)zero).Should().Be(0m);
        zero.Should().Be(new Money(0m));
    }

    [Fact]
    public void should_validate_always_returns_ok()
    {
        // given
        var values = new[] { -100m, 0m, 100m, decimal.MaxValue, decimal.MinValue };

        // when & then
        foreach (var value in values)
        {
            Money.Validate(value).Should().Be(PrimitiveValidationResult.Ok);
        }
    }

    [Fact]
    public void should_serialize_to_json()
    {
        // given
        var money = new Money(99.99m);

        // when
        var json = JsonSerializer.Serialize(money);

        // then
        json.Should().Be("99.99");
    }

    [Fact]
    public void should_deserialize_from_json()
    {
        // given
        const string json = "123.45";

        // when
        var money = JsonSerializer.Deserialize<Money>(json);

        // then
        money.Should().Be(new Money(123.45m));
    }
}
