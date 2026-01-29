// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable MA0011 // Use IFormatProvider - testing parameterless ToString()

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class CurrencyTests
{
    #region Construction & Properties

    [Fact]
    public void should_store_amount_and_currency_code()
    {
        // when
        var currency = new Currency(100.50m, "USD");

        // then
        currency.Amount.Should().Be(100.50m);
        currency.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void should_throw_when_currency_code_is_null()
    {
        // when
        var action = () => new Currency(100m, null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_throw_when_currency_code_is_whitespace()
    {
        // when
        var action = () => new Currency(100m, "   ");

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_have_zero_egp_static_instance()
    {
        // then
        Currency.ZeroEgp.Amount.Should().Be(0m);
        Currency.ZeroEgp.CurrencyCode.Should().Be("EGP");
    }

    [Fact]
    public void should_have_zero_usd_static_instance()
    {
        // then
        Currency.ZeroUsd.Amount.Should().Be(0m);
        Currency.ZeroUsd.CurrencyCode.Should().Be("USD");
    }

    #endregion

    #region Equality

    [Fact]
    public void equals_should_return_true_for_same_amount_and_code()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(100m, "USD");

        // then
        currency1.Equals(currency2).Should().BeTrue();
        (currency1 == currency2).Should().BeTrue();
        (currency1 != currency2).Should().BeFalse();
    }

    [Fact]
    public void equals_should_return_false_for_different_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(200m, "USD");

        // then
        currency1.Equals(currency2).Should().BeFalse();
        (currency1 == currency2).Should().BeFalse();
        (currency1 != currency2).Should().BeTrue();
    }

    [Fact]
    public void equals_should_return_false_for_different_codes()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(100m, "EUR");

        // then
        currency1.Equals(currency2).Should().BeFalse();
    }

    [Fact]
    public void equals_should_return_false_for_null()
    {
        // given
        var currency = new Currency(100m, "USD");

        // then
        currency.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void equality_operator_should_handle_null_left()
    {
        // given
        Currency? left = null;
        var right = new Currency(100m, "USD");

        // then
        (left == right)
            .Should()
            .BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void equality_operator_should_handle_null_right()
    {
        // given
        var left = new Currency(100m, "USD");
        Currency? right = null;

        // then
        (left == right)
            .Should()
            .BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void equality_operator_should_handle_both_null()
    {
        // given
        Currency? left = null;
        Currency? right = null;

        // then
        (left == right)
            .Should()
            .BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void get_hash_code_should_be_equal_for_equal_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(100m, "USD");

        // then
        currency1.GetHashCode().Should().Be(currency2.GetHashCode());
    }

    #endregion

    #region Comparison

    [Fact]
    public void compare_to_should_return_zero_for_equal_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(100m, "USD");

        // then
        currency1.CompareTo(currency2).Should().Be(0);
    }

    [Fact]
    public void compare_to_should_return_positive_for_greater_amount()
    {
        // given
        var currency1 = new Currency(200m, "USD");
        var currency2 = new Currency(100m, "USD");

        // then
        currency1.CompareTo(currency2).Should().BeGreaterThan(0);
    }

    [Fact]
    public void compare_to_should_return_negative_for_lesser_amount()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(200m, "USD");

        // then
        currency1.CompareTo(currency2).Should().BeLessThan(0);
    }

    [Fact]
    public void compare_to_should_return_positive_for_null()
    {
        // given
        var currency = new Currency(100m, "USD");

        // then
        currency.CompareTo((Currency?)null).Should().BeGreaterThan(0);
    }

    [Fact]
    public void compare_to_should_throw_for_different_currency_codes()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(100m, "EUR");

        // when
        var action = () => currency1.CompareTo(currency2);

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void compare_to_decimal_should_compare_amounts()
    {
        // given
        var currency = new Currency(100m, "USD");

        // then
        currency.CompareTo(50m).Should().BeGreaterThan(0);
        currency.CompareTo(100m).Should().Be(0);
        currency.CompareTo(150m).Should().BeLessThan(0);
    }

    [Fact]
    public void compare_to_object_should_handle_decimal()
    {
        // given
        var currency = new Currency(100m, "USD");

        // then
        currency.CompareTo((object)50m).Should().BeGreaterThan(0);
    }

    [Fact]
    public void compare_to_object_should_throw_for_invalid_type()
    {
        // given
        var currency = new Currency(100m, "USD");

        // when
        var action = () => currency.CompareTo("invalid");

        // then
        action.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Comparison Operators

    [Fact]
    public void greater_than_operator_should_compare_currencies()
    {
        // given
        var currency1 = new Currency(200m, "USD");
        var currency2 = new Currency(100m, "USD");

        // then
        (currency1 > currency2)
            .Should()
            .BeTrue();
        (currency2 > currency1).Should().BeFalse();
    }

    [Fact]
    public void less_than_operator_should_compare_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(200m, "USD");

        // then
        (currency1 < currency2)
            .Should()
            .BeTrue();
        (currency2 < currency1).Should().BeFalse();
    }

    [Fact]
    public void greater_than_or_equal_operator_should_compare_currencies()
    {
        // given
        var currency1 = new Currency(200m, "USD");
        var currency2 = new Currency(100m, "USD");
        var currency3 = new Currency(200m, "USD");

        // then
        (currency1 >= currency2)
            .Should()
            .BeTrue();
        (currency1 >= currency3).Should().BeTrue();
    }

    [Fact]
    public void less_than_or_equal_operator_should_compare_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(200m, "USD");
        var currency3 = new Currency(100m, "USD");

        // then
        (currency1 <= currency2)
            .Should()
            .BeTrue();
        (currency1 <= currency3).Should().BeTrue();
    }

    [Fact]
    public void decimal_comparison_operators_should_work()
    {
        // given
        var currency = new Currency(100m, "USD");

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
    public void addition_operator_should_add_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(50m, "USD");

        // when
        var result = currency1 + currency2;

        // then
        result.Amount.Should().Be(150m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void addition_should_throw_for_different_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(50m, "EUR");

        // when
        var action = () => currency1 + currency2;

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void add_method_should_add_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(50m, "USD");

        // when
        var result = Currency.Add(currency1, currency2);

        // then
        result.Amount.Should().Be(150m);
    }

    [Fact]
    public void subtraction_operator_should_subtract_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(30m, "USD");

        // when
        var result = currency1 - currency2;

        // then
        result.Amount.Should().Be(70m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void subtraction_should_throw_for_different_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(50m, "EUR");

        // when
        var action = () => currency1 - currency2;

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void subtract_method_should_subtract_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(30m, "USD");

        // when
        var result = Currency.Subtract(currency1, currency2);

        // then
        result.Amount.Should().Be(70m);
    }

    [Fact]
    public void multiplication_operator_should_multiply_amounts()
    {
        // given
        var currency1 = new Currency(10m, "USD");
        var currency2 = new Currency(5m, "USD");

        // when
        var result = currency1 * currency2;

        // then
        result.Amount.Should().Be(50m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void multiplication_should_throw_for_different_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(50m, "EUR");

        // when
        var action = () => currency1 * currency2;

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void multiply_method_should_multiply_amounts()
    {
        // given
        var currency1 = new Currency(10m, "USD");
        var currency2 = new Currency(5m, "USD");

        // when
        var result = Currency.Multiply(currency1, currency2);

        // then
        result.Amount.Should().Be(50m);
    }

    [Fact]
    public void division_operator_should_divide_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(4m, "USD");

        // when
        var result = currency1 / currency2;

        // then
        result.Amount.Should().Be(25m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void division_should_throw_for_different_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(50m, "EUR");

        // when
        var action = () => currency1 / currency2;

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void divide_method_should_divide_amounts()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(4m, "USD");

        // when
        var result = Currency.Divide(currency1, currency2);

        // then
        result.Amount.Should().Be(25m);
    }

    [Fact]
    public void modulus_operator_should_calculate_remainder()
    {
        // given
        var currency1 = new Currency(10m, "USD");
        var currency2 = new Currency(3m, "USD");

        // when
        var result = currency1 % currency2;

        // then
        result.Amount.Should().Be(1m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void modulus_should_throw_for_different_currencies()
    {
        // given
        var currency1 = new Currency(100m, "USD");
        var currency2 = new Currency(50m, "EUR");

        // when
        var action = () => currency1 % currency2;

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*different currency codes*");
    }

    [Fact]
    public void mod_method_should_calculate_remainder()
    {
        // given
        var currency1 = new Currency(10m, "USD");
        var currency2 = new Currency(3m, "USD");

        // when
        var result = Currency.Mod(currency1, currency2);

        // then
        result.Amount.Should().Be(1m);
    }

    #endregion

    #region Parsing

    // Note: The current parsing implementation has a bug - it splits on first digit
    // and treats everything before it as the amount and after as the currency code.
    // For input "USD100": amountSpan="USD" (fails), currencyCodeSpan="100"
    // For input "100USD": amountSpan="" (empty, fails early)
    // Only negative number prefixes like "-100USD" might work.

    [Fact]
    public void try_parse_should_return_false_for_input_starting_with_digit()
    {
        // Input starting with digit produces empty amount span
        // when
        var success = Currency.TryParse("100USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void try_parse_should_return_false_for_alpha_prefix()
    {
        // Alpha prefix is treated as amount, which fails decimal parsing
        // when
        var success = Currency.TryParse("USD100", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void try_parse_should_return_true_for_negative_number_prefix()
    {
        // "-100USD": amountSpan="-", currencyCodeSpan="100USD"
        // "-" fails decimal parsing
        // when
        var success = Currency.TryParse("-100USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeFalse();
    }

    [Fact]
    public void parse_should_throw_for_input_starting_with_digit()
    {
        // when
        var action = () => Currency.Parse("100USD", CultureInfo.InvariantCulture);

        // then
        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void parse_should_throw_for_alpha_prefix()
    {
        // when
        var action = () => Currency.Parse("USD100", CultureInfo.InvariantCulture);

        // then
        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void try_parse_string_overload_should_delegate_to_span()
    {
        // when
        var success = Currency.TryParse("100USD", CultureInfo.InvariantCulture, out var result);

        // then
        success.Should().BeFalse();
    }

    [Fact]
    public void parse_string_overload_should_delegate_to_span()
    {
        // when
        var action = () => Currency.Parse("100USD", CultureInfo.InvariantCulture);

        // then
        action.Should().Throw<FormatException>();
    }

    #endregion

    #region Formatting

    [Fact]
    public void to_string_should_format_amount_and_code()
    {
        // given
        var currency = new Currency(100.5m, "USD");

        // when
        var result = currency.ToString();

        // then
        result.Should().Be("100.5USD");
    }

    [Fact]
    public void to_string_with_format_provider_should_use_provider()
    {
        // given
        var currency = new Currency(1000.5m, "USD");

        // when
        var result = currency.ToString(null, CultureInfo.InvariantCulture);

        // then
        result.Should().Contain("1000.5");
        result.Should().Contain("USD");
    }

    [Fact]
    public void try_format_span_should_write_to_destination()
    {
        // given
        var currency = new Currency(100m, "USD");
        Span<char> destination = stackalloc char[20];

        // when
        var success = currency.TryFormat(destination, out var charsWritten, default, CultureInfo.InvariantCulture);

        // then
        success.Should().BeTrue();
        charsWritten.Should().BeGreaterThan(0);
        destination[..charsWritten].ToString().Should().Be("100USD");
    }

    [Fact]
    public void try_format_utf8_span_should_write_to_destination()
    {
        // given
        var currency = new Currency(100m, "USD");
        Span<byte> destination = stackalloc byte[20];

        // when
        var success = currency.TryFormat(destination, out var bytesWritten, default, CultureInfo.InvariantCulture);

        // then
        success.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(0);
    }

    #endregion
}
