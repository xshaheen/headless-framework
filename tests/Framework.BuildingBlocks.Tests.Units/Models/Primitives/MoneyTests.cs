// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives;
using Framework.Primitives;

namespace Tests.Models.Primitives;

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
}
