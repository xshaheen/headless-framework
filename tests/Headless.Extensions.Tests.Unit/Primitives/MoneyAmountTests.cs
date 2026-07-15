// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;
using MoneyAmount = Headless.Primitives.MoneyAmount;

namespace Tests.Primitives;

public sealed class MoneyAmountTests
{
    [Fact]
    public void should_be_zero_when_money_zero()
    {
        // given & when
        var result = MoneyAmount.Zero;

        // then
        result.Should().Be(new MoneyAmount(0));
    }

    [Fact]
    public void should_round_up_to_two_decimal_places_when_get_rounded()
    {
        // given
        var money = new MoneyAmount(5.678m);

        // when
        var result = money.GetRounded();

        // then
        result.Should().Be(new MoneyAmount(5.68m));
    }

    [Fact]
    public void should_return_ok_for_valid_value_when_validate()
    {
        // given
        const decimal value = 10m;

        // when
        var result = MoneyAmount.Validate(value);

        // then
        result.Should().Be(PrimitiveValidationResult.Ok);
    }

    [Theory]
    // Banker's rounding (MidpointRounding.ToEven): a midpoint rounds to the nearest even last digit.
    [InlineData(5.125, 5.12)] // preceding digit 2 is even -> stays 5.12
    [InlineData(5.135, 5.14)] // preceding digit 3 is odd  -> rounds to even 5.14
    [InlineData(2.675, 2.68)] // preceding digit 7 is odd  -> rounds to even 2.68
    public void should_round_midpoints_to_even(decimal value, decimal expected)
    {
        // given - value at the two-decimal midpoint
        var money = new MoneyAmount(value);

        // when
        var result = money.GetRounded();

        // then - banker's rounding
        result.Should().Be(new MoneyAmount(expected));
    }

    [Fact]
    public void should_round_to_two_decimal_places()
    {
        // given
        var money = new MoneyAmount(123.456789m);

        // when
        var result = money.GetRounded();

        // then
        result.Should().Be(new MoneyAmount(123.46m));
    }

    [Fact]
    public void should_return_zero_static_instance()
    {
        // given & when
        var zero = MoneyAmount.Zero;

        // then
        ((decimal)zero)
            .Should()
            .Be(0m);
        zero.Should().Be(new MoneyAmount(0m));
    }

    [Fact]
    public void should_validate_always_returns_ok()
    {
        // given
        var values = new[] { -100m, 0m, 100m, decimal.MaxValue, decimal.MinValue };

        // when & then
        foreach (var value in values)
        {
            MoneyAmount.Validate(value).Should().Be(PrimitiveValidationResult.Ok);
        }
    }

    [Fact]
    public void should_serialize_to_json()
    {
        // given
        var money = new MoneyAmount(99.99m);

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
        var money = JsonSerializer.Deserialize<MoneyAmount>(json);

        // then
        money.Should().Be(new MoneyAmount(123.45m));
    }

    [Fact]
    public void should_not_throw_and_return_zero_when_default_get_hash_code()
    {
        // given
        var act = () => default(MoneyAmount).GetHashCode();

        // when & then
        act.Should().NotThrow().Which.Should().Be(0);
    }

    [Fact]
    public void should_equal_default_but_not_an_initialized_value_when_default()
    {
        // given
        var uninitialized = default(MoneyAmount);
        var initialized = new MoneyAmount(0m);

        // when & then
        uninitialized.Equals(default).Should().BeTrue();
        (uninitialized == default).Should().BeTrue();
        uninitialized.Equals(initialized).Should().BeFalse();
        (uninitialized != initialized).Should().BeTrue();
    }

    [Fact]
    public void should_compare_consistently_with_equals_when_default()
    {
        // given
        var uninitialized = default(MoneyAmount);
        var initialized = new MoneyAmount(0m);

        // when & then
        uninitialized.CompareTo(default).Should().Be(0); // both uninitialized => equal
        uninitialized.CompareTo(initialized).Should().Be(-1); // uninitialized sorts first
        initialized.CompareTo(uninitialized).Should().Be(1);
    }

    [Fact]
    public void should_be_usable_as_a_hash_set_member_when_default()
    {
        // given & when
        var set = new HashSet<MoneyAmount> { default, default, new(0m) };

        // then - two defaults collapse to one entry, distinct from the initialized zero value
        set.Should().HaveCount(2);
        set.Should().Contain(default(MoneyAmount));
    }
}
