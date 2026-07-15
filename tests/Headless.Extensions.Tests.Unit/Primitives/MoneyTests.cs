// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable MA0011 // Use IFormatProvider - testing parameterless ToString()

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class MoneyTests
{
    #region Construction & Properties

    [Fact]
    public void should_store_amount_and_currency_code()
    {
        // when
        var currency = new Money(100.50m, "USD");

        // then
        currency.Amount.Should().Be(100.50m);
        currency.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_throw_when_currency_code_is_null()
    {
        // when
        var action = () => new Money(100m, null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_throw_when_currency_code_is_whitespace()
    {
        // when
        var action = () => new Money(100m, "   ");

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_have_zero_egp_static_instance()
    {
        // then
        Money.ZeroEgp.Amount.Should().Be(0m);
        Money.ZeroEgp.CurrencyCode.Should().Be("EGP");
    }

    [Fact]
    public void should_have_zero_usd_static_instance()
    {
        // then
        Money.ZeroUsd.Amount.Should().Be(0m);
        Money.ZeroUsd.CurrencyCode.Should().Be("USD");
    }

    #endregion

    #region Equality

    [Fact]
    public void should_return_true_for_same_amount_and_code_when_equals()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(100m, "USD");

        // then
        currency1.Equals(currency2).Should().BeTrue();
        (currency1 == currency2).Should().BeTrue();
        (currency1 != currency2).Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_different_amounts_when_equals()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(200m, "USD");

        // then
        currency1.Equals(currency2).Should().BeFalse();
        (currency1 == currency2).Should().BeFalse();
        (currency1 != currency2).Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_different_codes_when_equals()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(100m, "EUR");

        // then
        currency1.Equals(currency2).Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_null_when_equals()
    {
        // given
        var currency = new Money(100m, "USD");

        // then
        currency.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void should_handle_null_left_when_equality_operator()
    {
        // given
        Money? left = null;
        var right = new Money(100m, "USD");

        // then
        (left == right)
            .Should()
            .BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void should_handle_null_right_when_equality_operator()
    {
        // given
        var left = new Money(100m, "USD");
        Money? right = null;

        // then
        (left == right)
            .Should()
            .BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void should_handle_both_null_when_equality_operator()
    {
        // given
        Money? left = null;
        Money? right = null;

        // then
        (left == right)
            .Should()
            .BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void should_be_equal_for_equal_currencies_when_get_hash_code()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(100m, "USD");

        // then
        currency1.GetHashCode().Should().Be(currency2.GetHashCode());
    }

    #endregion

    #region Comparison

    [Fact]
    public void should_return_zero_for_equal_amounts_when_compare_to()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(100m, "USD");

        // then
        currency1.CompareTo(currency2).Should().Be(0);
    }

    [Fact]
    public void should_return_positive_for_greater_amount_when_compare_to()
    {
        // given
        var currency1 = new Money(200m, "USD");
        var currency2 = new Money(100m, "USD");

        // then
        currency1.CompareTo(currency2).Should().BePositive();
    }

    [Fact]
    public void should_return_negative_for_lesser_amount_when_compare_to()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(200m, "USD");

        // then
        currency1.CompareTo(currency2).Should().BeNegative();
    }

    [Fact]
    public void should_return_positive_for_null_when_compare_to()
    {
        // given
        var currency = new Money(100m, "USD");

        // then
        currency.CompareTo(null).Should().BePositive();
    }

    [Fact]
    public void should_order_by_code_then_amount_across_currencies_when_compare_to()
    {
        // given - EUR sorts before USD ordinally, regardless of amount
        var eur = new Money(1000m, "EUR");
        var usd = new Money(1m, "USD");

        // then - total ordering: code first, so it never throws on a code mismatch
        eur.CompareTo(usd).Should().BeNegative();
        usd.CompareTo(eur).Should().BePositive();
    }

    [Fact]
    public void should_order_by_amount_within_same_code_when_compare_to()
    {
        // given
        var small = new Money(100m, "USD");
        var large = new Money(200m, "USD");

        // then
        small.CompareTo(large).Should().BeNegative();
    }

    [Fact]
    public void should_not_throw_for_mixed_currency_codes_when_sort()
    {
        // given - the old behavior threw from List.Sort on a code mismatch
        var list = new List<Money> { new(5m, "USD"), new(10m, "EUR"), new(1m, "USD"), new(2m, "EUR") };

        // when
        var sort = () => list.Sort();

        // then - total ordering keeps Sort safe; result is grouped by code then ascending amount
        sort.Should().NotThrow();
        list.Select(c => c.ToString()).Should().ContainInOrder("2EUR", "10EUR", "1USD", "5USD");
    }

    [Fact]
    public void should_compare_amounts_when_compare_to_decimal()
    {
        // given
        var currency = new Money(100m, "USD");

        // then
        currency.CompareTo(50m).Should().BePositive();
        currency.CompareTo(100m).Should().Be(0);
        currency.CompareTo(150m).Should().BeNegative();
    }

    [Fact]
    public void should_handle_decimal_when_compare_to_object()
    {
        // given
        var currency = new Money(100m, "USD");

        // then
        currency.CompareTo((object)50m).Should().BePositive();
    }

    [Fact]
    public void should_throw_for_invalid_type_when_compare_to_object()
    {
        // given
        var currency = new Money(100m, "USD");

        // when
        var action = () => currency.CompareTo("invalid");

        // then
        action.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Comparison Operators

    [Fact]
    public void should_compare_currencies_when_greater_than_operator()
    {
        // given
        var currency1 = new Money(200m, "USD");
        var currency2 = new Money(100m, "USD");

        // then
        (currency1 > currency2)
            .Should()
            .BeTrue();
        (currency2 > currency1).Should().BeFalse();
    }

    [Fact]
    public void should_compare_currencies_when_less_than_operator()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(200m, "USD");

        // then
        (currency1 < currency2)
            .Should()
            .BeTrue();
        (currency2 < currency1).Should().BeFalse();
    }

    [Fact]
    public void should_compare_currencies_when_greater_than_or_equal_operator()
    {
        // given
        var currency1 = new Money(200m, "USD");
        var currency2 = new Money(100m, "USD");
        var currency3 = new Money(200m, "USD");

        // then
        (currency1 >= currency2)
            .Should()
            .BeTrue();
        (currency1 >= currency3).Should().BeTrue();
    }

    [Fact]
    public void should_compare_currencies_when_less_than_or_equal_operator()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(200m, "USD");
        var currency3 = new Money(100m, "USD");

        // then
        (currency1 <= currency2)
            .Should()
            .BeTrue();
        (currency1 <= currency3).Should().BeTrue();
    }

    [Fact]
    public void should_work_when_decimal_comparison_operators()
    {
        // given
        var currency = new Money(100m, "USD");

        // then
        (currency > 50m)
            .Should()
            .BeTrue();
        (currency < 150m).Should().BeTrue();
        (currency >= 100m).Should().BeTrue();
        (currency <= 100m).Should().BeTrue();
        (50m < currency).Should().BeTrue();
        (150m > currency).Should().BeTrue();
        (100m >= currency).Should().BeTrue();
        (100m <= currency).Should().BeTrue();
    }

    #endregion

    #region Math Operators

    [Fact]
    public void should_add_amounts_when_addition_operator()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(50m, "USD");

        // when
        var result = currency1 + currency2;

        // then
        result.Amount.Should().Be(150m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_throw_for_different_currencies_when_addition()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(50m, "EUR");

        // when
        var action = () => currency1 + currency2;

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void should_add_amounts_when_add_method()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(50m, "USD");

        // when
        var result = Money.Add(currency1, currency2);

        // then
        result.Amount.Should().Be(150m);
    }

    [Fact]
    public void should_subtract_amounts_when_subtraction_operator()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(30m, "USD");

        // when
        var result = currency1 - currency2;

        // then
        result.Amount.Should().Be(70m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_throw_for_different_currencies_when_subtraction()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(50m, "EUR");

        // when
        var action = () => currency1 - currency2;

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void should_subtract_amounts_when_subtract_method()
    {
        // given
        var currency1 = new Money(100m, "USD");
        var currency2 = new Money(30m, "USD");

        // when
        var result = Money.Subtract(currency1, currency2);

        // then
        result.Amount.Should().Be(70m);
    }

    [Fact]
    public void should_scale_amount_by_decimal_when_multiplication_operator()
    {
        // given
        var currency = new Money(10m, "USD");

        // when
        var result = currency * 5m;

        // then
        result.Amount.Should().Be(50m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_scale_amount_and_round_to_even_when_multiply_method()
    {
        // given - 10.005 * 1 sits at the cent midpoint and rounds to even (10.00)
        var currency = new Money(10.005m, "USD");

        // when
        var result = Money.Multiply(currency, 1m);

        // then
        result.Amount.Should().Be(10.00m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_honor_explicit_rounding_mode_when_multiply_method()
    {
        // given
        var currency = new Money(10.005m, "USD");

        // when
        var result = Money.Multiply(currency, 1m, MidpointRounding.AwayFromZero);

        // then - away-from-zero rounds the cent up
        result.Amount.Should().Be(10.01m);
    }

    [Fact]
    public void should_scale_amount_by_decimal_when_division_operator()
    {
        // given
        var currency = new Money(100m, "USD");

        // when
        var result = currency / 4m;

        // then
        result.Amount.Should().Be(25m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_round_sub_cents_to_the_currency_scale_when_division()
    {
        // given - 10 / 3 = 3.3333... rounds to two decimals
        var currency = new Money(10m, "USD");

        // when
        var result = currency / 3m;

        // then
        result.Amount.Should().Be(3.33m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_honor_explicit_rounding_mode_when_divide_method()
    {
        // given - 2 / 3 = 0.6666...
        var currency = new Money(2m, "USD");

        // when
        var result = Money.Divide(currency, 3m, MidpointRounding.ToZero);

        // then - truncates toward zero at the cent scale
        result.Amount.Should().Be(0.66m);
    }

    [Fact]
    public void should_throw_when_division_by_zero()
    {
        // given
        var currency = new Money(100m, "USD");

        // when
        var action = () => currency / 0m;

        // then
        action.Should().Throw<DivideByZeroException>();
    }

    #endregion

    #region Parsing

    [Fact]
    public void should_parse_amount_and_currency_code_when_try_parse()
    {
        // when
        var success = Money.TryParse("100USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Amount.Should().Be(100m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_parse_decimal_amount_when_try_parse()
    {
        // when
        var success = Money.TryParse("100.5USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeTrue();
        result!.Amount.Should().Be(100.5m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_parse_negative_amount_when_try_parse()
    {
        // when
        var success = Money.TryParse("-100USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeTrue();
        result!.Amount.Should().Be(-100m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_round_trip_to_string_output_when_try_parse()
    {
        // given
        var original = new Money(1234.56m, "EUR");

        // when
        var success = Money.TryParse(original.ToString(), CultureInfo.InvariantCulture, out var parsed);

        // then
        success.Should().BeTrue();
        parsed.Should().Be(original);
    }

    [Fact]
    public void should_scan_code_from_the_end_and_keep_exponent_in_amount_when_try_parse()
    {
        // when - the trailing letter run "USD" is the code; the "1E5" prefix parses as the amount (100000)
        var success = Money.TryParse("1E5USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeTrue();
        result!.Amount.Should().Be(100_000m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_return_false_for_malformed_amount_when_try_parse()
    {
        // when - "10.5.5" is not a valid decimal prefix
        var success = Money.TryParse("10.5.5USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_false_when_try_parse_currency_code_is_missing()
    {
        // when
        var success = Money.TryParse("100", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_false_when_try_parse_amount_is_missing()
    {
        // when
        var success = Money.TryParse("USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_currency_for_valid_input_when_parse()
    {
        // when
        var result = Money.Parse("100USD", CultureInfo.InvariantCulture);

        // then
        result.Amount.Should().Be(100m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_throw_for_input_without_a_currency_code_when_parse()
    {
        // when
        var action = () => Money.Parse("100", CultureInfo.InvariantCulture);

        // then
        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void should_delegate_to_span_when_try_parse_string_overload()
    {
        // when
        var success = Money.TryParse("100USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeTrue();
        result!.CurrencyCode.Should().Be("USD");
    }

    #endregion

    #region Formatting

    [Fact]
    public void should_format_amount_and_code_when_to_string()
    {
        // given
        var currency = new Money(100.5m, "USD");

        // when
        var result = currency.ToString();

        // then
        result.Should().Be("100.5USD");
    }

    [Fact]
    public void should_use_provider_when_to_string_with_format_provider()
    {
        // given
        var currency = new Money(1000.5m, "USD");

        // when
        var result = currency.ToString(null, CultureInfo.InvariantCulture);

        // then
        result.Should().Contain("1000.5");
        result.Should().Contain("USD");
    }

    [Fact]
    public void should_write_to_destination_when_try_format_span()
    {
        // given
        var currency = new Money(100m, "USD");
        Span<char> destination = stackalloc char[20];

        // when
        var success = currency.TryFormat(destination, out var charsWritten, default, CultureInfo.InvariantCulture);

        // then
        success.Should().BeTrue();
        charsWritten.Should().BePositive();
        destination[..charsWritten].ToString().Should().Be("100USD");
    }

    [Fact]
    public void should_write_to_destination_when_try_format_utf8_span()
    {
        // given
        var currency = new Money(100m, "USD");
        Span<byte> destination = stackalloc byte[20];

        // when
        var success = currency.TryFormat(destination, out var bytesWritten, default, CultureInfo.InvariantCulture);

        // then
        success.Should().BeTrue();
        bytesWritten.Should().BePositive();
    }

    #endregion
}
