// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Unicode;
using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// A monetary amount paired with a currency code (for example <c>10.50USD</c>). Additive operations
/// (<c>+</c>, <c>-</c>) require both operands to share the same <see cref="CurrencyCode"/>; scaling
/// operations (<c>*</c>, <c>/</c>) take a <see cref="decimal"/> factor and preserve the currency code.
/// Use <see cref="MoneyAmount"/> when a bare decimal amount without a currency code is enough.
/// </summary>
/// <param name="amount">The monetary amount.</param>
/// <param name="currencyCode">The currency code (for example <c>"USD"</c>). Must be a non-blank string.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="currencyCode"/> is <see langword="null"/>.</exception>
/// <exception cref="ArgumentException">Thrown when <paramref name="currencyCode"/> is empty or white space.</exception>
[PublicAPI]
[ComplexType]
[DebuggerDisplay("{" + nameof(Amount) + "}{" + nameof(CurrencyCode) + "}")]
public sealed class Money(decimal amount, string currencyCode)
    : IComparable,
        IComparable<Money>,
        IComparable<decimal>,
        IAdditionOperators<Money, Money, Money>,
        ISubtractionOperators<Money, Money, Money>,
        IMultiplyOperators<Money, decimal, Money>,
        IDivisionOperators<Money, decimal, Money>,
        IComparisonOperators<Money, Money, bool>,
        ISpanParsable<Money?>,
        IUtf8SpanFormattable,
        ISpanFormattable,
        IEquatable<Money?>
{
    #region Static

    /// <summary>A zero-amount <see cref="Money"/> in Egyptian pounds (<c>EGP</c>).</summary>
    public static readonly Money ZeroEgp = new(0, "EGP");

    /// <summary>A zero-amount <see cref="Money"/> in US dollars (<c>USD</c>).</summary>
    public static readonly Money ZeroUsd = new(0, "USD");

    // Number of fractional digits a scaling result is rounded to (cents). Money carries no per-code
    // scale table, so the conventional minor-unit scale of 2 is used (matching MoneyAmount.GetRounded).
    private const int _Scale = 2;

    #endregion

    #region Props

    /// <summary>The monetary amount.</summary>
    public decimal Amount { get; private init; } = amount;

    /// <summary>The currency code (for example <c>"USD"</c>).</summary>
    public string CurrencyCode { get; private init; } = Argument.IsNotNullOrWhiteSpace(currencyCode);

    #endregion

    #region IEquatable Implementation

    /// <summary>Determines whether <paramref name="obj"/> is a <see cref="Money"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with this instance.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="Money"/> with the same amount and currency code; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj)
    {
        return obj is Money other && Equals(other);
    }

    /// <summary>Determines whether <paramref name="other"/> has the same amount and currency code as this instance.</summary>
    /// <param name="other">The currency to compare with this instance.</param>
    /// <returns><see langword="true"/> if both have the same amount and currency code; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Money? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(CurrencyCode, other.CurrencyCode, StringComparison.Ordinal) && other.Amount == Amount;
    }

    /// <summary>Determines whether two <see cref="Money"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if both are <see langword="null"/> or have the same amount and currency code; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Money? left, Money? right) => left?.Equals(right) ?? (right is null);

    /// <summary>Determines whether two <see cref="Money"/> instances are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances differ in amount or currency code; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Money? left, Money? right) => !(left == right);

    #endregion

    #region IComparable Implementation

    /// <summary>Compares this instance with <paramref name="obj"/> and returns their relative order.</summary>
    /// <param name="obj">The object to compare with. Supported types are <see cref="decimal"/> and <see cref="Money"/>; <see langword="null"/> sorts first.</param>
    /// <returns>A negative value if this instance precedes <paramref name="obj"/>, zero if they are equal, or a positive value if it follows.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="obj"/> is neither <see langword="null"/>, a <see cref="decimal"/>, nor a <see cref="Money"/>.</exception>
    public int CompareTo(object? obj)
    {
        return obj switch
        {
            null => 1,
            decimal d => CompareTo(d),
            Money c => CompareTo(c),
            _ => throw new ArgumentException($"Object is not a {nameof(Money)}", nameof(obj)),
        };
    }

    /// <summary>
    /// Compares this instance with <paramref name="other"/> and returns their relative order. The ordering is
    /// total: instances are ordered by <see cref="CurrencyCode"/> (ordinal) first, then by <see cref="Amount"/>.
    /// This makes <see cref="Money"/> safe to sort even across mixed currency codes; it does not imply the
    /// amounts are comparable in value.
    /// </summary>
    /// <param name="other">The currency to compare with. <see langword="null"/> sorts first.</param>
    /// <returns>A negative value if this instance precedes <paramref name="other"/>, zero if equal, or a positive value if it follows.</returns>
    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        var codeComparison = string.CompareOrdinal(CurrencyCode, other.CurrencyCode);

        return codeComparison != 0 ? codeComparison : Amount.CompareTo(other.Amount);
    }

    /// <summary>Compares this instance's <see cref="Amount"/> with <paramref name="other"/> and returns their relative order.</summary>
    /// <param name="other">The amount to compare with.</param>
    /// <returns>A negative value if <see cref="Amount"/> is less than <paramref name="other"/>, zero if equal, or a positive value if greater.</returns>
    public int CompareTo(decimal other)
    {
        return Amount.CompareTo(other);
    }

    #endregion

    #region IParsable Implementation

    /// <summary>Parses a string in the form <c>{amount}{code}</c> (for example <c>10.50USD</c>) into a <see cref="Money"/>.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider used to parse the numeric amount.</param>
    /// <returns>The parsed <see cref="Money"/>.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="s"/> is not a valid currency string.</exception>
    public static Money Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    /// <summary>Attempts to parse a string in the form <c>{amount}{code}</c> into a <see cref="Money"/>.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider used to parse the numeric amount.</param>
    /// <param name="result">When this method returns <see langword="true"/>, the parsed <see cref="Money"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="s"/> was parsed successfully; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? s, IFormatProvider? provider, [NotNullWhen(true)] out Money? result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    /// <summary>Parses a span in the form <c>{amount}{code}</c> (for example <c>10.50USD</c>) into a <see cref="Money"/>.</summary>
    /// <param name="s">The span to parse.</param>
    /// <param name="provider">An optional format provider used to parse the numeric amount.</param>
    /// <returns>The parsed <see cref="Money"/>.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="s"/> is not a valid currency string.</exception>
    public static Money Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out var result) ? result : throw new FormatException("Invalid currency format");
    }

    /// <summary>Attempts to parse a span in the form <c>{amount}{code}</c> into a <see cref="Money"/>.</summary>
    /// <param name="s">The span to parse.</param>
    /// <param name="provider">An optional format provider used to parse the numeric amount.</param>
    /// <param name="result">When this method returns <see langword="true"/>, the parsed <see cref="Money"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="s"/> was parsed successfully; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [NotNullWhen(true)] out Money? result)
    {
        // 1. Scan from the END for the trailing run of ASCII letters: that run is the currency code, and the
        //    prefix before it is the amount (format: "{amount}{code}"). Scanning from the end (instead of to the
        //    first letter) keeps an exponent in the amount, e.g. "1E5USD" -> amount "1E5", code "USD".
        var codeStartIndex = s.Length;

        while (codeStartIndex > 0 && char.IsAsciiLetter(s[codeStartIndex - 1]))
        {
            codeStartIndex--;
        }

        // No code (no trailing letters) or no amount (string is all letters) -> not a valid currency string.
        if (codeStartIndex == s.Length || codeStartIndex == 0)
        {
            result = null;

            return false;
        }

        // 2. Parse the whole amount prefix strictly; any malformed remainder fails the parse.
        if (!decimal.TryParse(s[..codeStartIndex], NumberStyles.Any, provider, out var amount))
        {
            result = null;

            return false;
        }

        // 3. Build the currency (the ctor validates the code is non-blank).
        result = new Money(amount, s[codeStartIndex..].ToString());

        return true;
    }

    #endregion

    #region ISpanFormattable & IUtf8SpanFormattable Implementation

    /// <summary>Writes the <c>{amount}{code}</c> representation into a UTF-16 character span.</summary>
    /// <param name="destination">The span to write into.</param>
    /// <param name="charsWritten">When this method returns, the number of characters written to <paramref name="destination"/>.</param>
    /// <param name="format">A format span (ignored; the fixed <c>{amount}{code}</c> layout is always used).</param>
    /// <param name="provider">An optional format provider used to format the amount.</param>
    /// <returns><see langword="true"/> if <paramref name="destination"/> was large enough; otherwise <see langword="false"/>.</returns>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider
    )
    {
        return destination.TryWrite(provider, $"{Amount}{CurrencyCode}", out charsWritten);
    }

    /// <summary>Writes the <c>{amount}{code}</c> representation into a UTF-8 byte span.</summary>
    /// <param name="utf8Destination">The span to write into.</param>
    /// <param name="bytesWritten">When this method returns, the number of bytes written to <paramref name="utf8Destination"/>.</param>
    /// <param name="format">A format span (ignored; the fixed <c>{amount}{code}</c> layout is always used).</param>
    /// <param name="provider">An optional format provider used to format the amount.</param>
    /// <returns><see langword="true"/> if <paramref name="utf8Destination"/> was large enough; otherwise <see langword="false"/>.</returns>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider
    )
    {
        return Utf8.TryWrite(utf8Destination, provider, $"{Amount}{CurrencyCode}", out bytesWritten);
    }

    #endregion

    #region Math Operators

    /// <summary>Adds two currencies of the same currency code.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A <see cref="Money"/> whose amount is the sum of the operands' amounts.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="left"/> and <paramref name="right"/> have different currency codes.</exception>
    public static Money operator +(Money left, Money right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot add currencies with different currency codes");
        }

        return new Money(left.Amount + right.Amount, left.CurrencyCode);
    }

    /// <summary>Adds two currencies of the same currency code. Named alternate for the <c>+</c> operator.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A <see cref="Money"/> whose amount is the sum of the operands' amounts.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="left"/> and <paramref name="right"/> have different currency codes.</exception>
    public static Money Add(Money left, Money right)
    {
        return left + right;
    }

    /// <summary>Subtracts one currency from another of the same currency code.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A <see cref="Money"/> whose amount is <paramref name="left"/> minus <paramref name="right"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="left"/> and <paramref name="right"/> have different currency codes.</exception>
    public static Money operator -(Money left, Money right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot subtract currencies with different currency codes");
        }

        return new Money(left.Amount - right.Amount, left.CurrencyCode);
    }

    /// <summary>Subtracts one currency from another of the same currency code. Named alternate for the <c>-</c> operator.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A <see cref="Money"/> whose amount is <paramref name="left"/> minus <paramref name="right"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="left"/> and <paramref name="right"/> have different currency codes.</exception>
    public static Money Subtract(Money left, Money right)
    {
        return left - right;
    }

    /// <summary>Scales a currency amount by a <see cref="decimal"/> factor, keeping the same currency code.</summary>
    /// <param name="left">The currency to scale.</param>
    /// <param name="right">The scalar factor.</param>
    /// <returns>
    /// A <see cref="Money"/> whose amount is <paramref name="left"/>'s amount multiplied by <paramref name="right"/>,
    /// rounded to the currency's minor-unit scale using <see cref="MidpointRounding.ToEven"/>.
    /// </returns>
    public static Money operator *(Money left, decimal right) => Multiply(left, right);

    /// <summary>Scales a currency amount by a <see cref="decimal"/> factor, keeping the same currency code (commutative form of <c>currency * factor</c>).</summary>
    /// <param name="left">The scalar factor.</param>
    /// <param name="right">The currency to scale.</param>
    /// <returns>
    /// A <see cref="Money"/> whose amount is <paramref name="right"/>'s amount multiplied by <paramref name="left"/>,
    /// rounded to the currency's minor-unit scale using <see cref="MidpointRounding.ToEven"/>.
    /// </returns>
    public static Money operator *(decimal left, Money right) => Multiply(right, left);

    /// <summary>Scales a currency amount by a <see cref="decimal"/> factor, keeping the same currency code.</summary>
    /// <param name="currency">The currency to scale.</param>
    /// <param name="factor">The scalar factor.</param>
    /// <param name="rounding">The rounding strategy applied to a fractional sub-unit result. Defaults to <see cref="MidpointRounding.ToEven"/>.</param>
    /// <returns>A <see cref="Money"/> whose amount is <paramref name="currency"/>'s amount multiplied by <paramref name="factor"/>, rounded to the currency's minor-unit scale.</returns>
    public static Money Multiply(Money currency, decimal factor, MidpointRounding rounding = MidpointRounding.ToEven)
    {
        var amount = Math.Round(currency.Amount * factor, _Scale, rounding);

        return new Money(amount, currency.CurrencyCode);
    }

    /// <summary>Divides a currency amount by a <see cref="decimal"/> divisor, keeping the same currency code.</summary>
    /// <param name="left">The currency to divide.</param>
    /// <param name="right">The scalar divisor.</param>
    /// <returns>
    /// A <see cref="Money"/> whose amount is <paramref name="left"/>'s amount divided by <paramref name="right"/>,
    /// rounded to the currency's minor-unit scale using <see cref="MidpointRounding.ToEven"/>.
    /// </returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="right"/> is zero.</exception>
    public static Money operator /(Money left, decimal right) => Divide(left, right);

    /// <summary>Divides a currency amount by a <see cref="decimal"/> divisor, keeping the same currency code.</summary>
    /// <param name="currency">The currency to divide.</param>
    /// <param name="divisor">The scalar divisor.</param>
    /// <param name="rounding">The rounding strategy applied to a fractional sub-unit result. Defaults to <see cref="MidpointRounding.ToEven"/>.</param>
    /// <returns>A <see cref="Money"/> whose amount is <paramref name="currency"/>'s amount divided by <paramref name="divisor"/>, rounded to the currency's minor-unit scale.</returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="divisor"/> is zero.</exception>
    public static Money Divide(Money currency, decimal divisor, MidpointRounding rounding = MidpointRounding.ToEven)
    {
        var amount = Math.Round(currency.Amount / divisor, _Scale, rounding);

        return new Money(amount, currency.CurrencyCode);
    }

    #endregion

    #region Comparison Operators

    /// <summary>Determines whether <paramref name="left"/> sorts after <paramref name="right"/> in the total order (by code, then amount).</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether <paramref name="left"/> sorts after or equal to <paramref name="right"/> in the total order (by code, then amount).</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>Determines whether <paramref name="left"/> sorts before <paramref name="right"/> in the total order (by code, then amount).</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether <paramref name="left"/> sorts before or equal to <paramref name="right"/> in the total order (by code, then amount).</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether the amount <paramref name="left"/> is greater than <paramref name="right"/>'s amount.</summary>
    /// <param name="left">The amount on the left.</param>
    /// <param name="right">The currency on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>'s amount; otherwise <see langword="false"/>.</returns>
    public static bool operator >(decimal left, Money right) => left > right.Amount;

    /// <summary>Determines whether the amount <paramref name="left"/> is less than <paramref name="right"/>'s amount.</summary>
    /// <param name="left">The amount on the left.</param>
    /// <param name="right">The currency on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>'s amount; otherwise <see langword="false"/>.</returns>
    public static bool operator <(decimal left, Money right) => left < right.Amount;

    /// <summary>Determines whether the amount <paramref name="left"/> is greater than or equal to <paramref name="right"/>'s amount.</summary>
    /// <param name="left">The amount on the left.</param>
    /// <param name="right">The currency on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>'s amount; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(decimal left, Money right) => left >= right.Amount;

    /// <summary>Determines whether the amount <paramref name="left"/> is less than or equal to <paramref name="right"/>'s amount.</summary>
    /// <param name="left">The amount on the left.</param>
    /// <param name="right">The currency on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>'s amount; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(decimal left, Money right) => left <= right.Amount;

    /// <summary>Determines whether <paramref name="left"/>'s amount is greater than the amount <paramref name="right"/>.</summary>
    /// <param name="left">The currency on the left.</param>
    /// <param name="right">The amount on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/>'s amount is greater than <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >(Money left, decimal right) => left.Amount > right;

    /// <summary>Determines whether <paramref name="left"/>'s amount is less than the amount <paramref name="right"/>.</summary>
    /// <param name="left">The currency on the left.</param>
    /// <param name="right">The amount on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/>'s amount is less than <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <(Money left, decimal right) => left.Amount < right;

    /// <summary>Determines whether <paramref name="left"/>'s amount is greater than or equal to the amount <paramref name="right"/>.</summary>
    /// <param name="left">The currency on the left.</param>
    /// <param name="right">The amount on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/>'s amount is greater than or equal to <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(Money left, decimal right) => left.Amount >= right;

    /// <summary>Determines whether <paramref name="left"/>'s amount is less than or equal to the amount <paramref name="right"/>.</summary>
    /// <param name="left">The currency on the left.</param>
    /// <param name="right">The amount on the right.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/>'s amount is less than or equal to <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(Money left, decimal right) => left.Amount <= right;

    #endregion

    #region Basic Methods

    /// <summary>Returns a hash code derived from the <see cref="Amount"/> and <see cref="CurrencyCode"/>.</summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Amount, CurrencyCode);
    }

    /// <summary>Returns the <c>{amount}{code}</c> representation formatted with the invariant culture.</summary>
    /// <returns>The string representation of this currency.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Amount}{CurrencyCode}");
    }

    /// <summary>Returns the <c>{amount}{code}</c> representation formatted with the supplied provider.</summary>
    /// <param name="format">A format string (ignored; the fixed <c>{amount}{code}</c> layout is always used).</param>
    /// <param name="formatProvider">An optional format provider used to format the amount.</param>
    /// <returns>The string representation of this currency.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return string.Create(formatProvider, $"{Amount}{CurrencyCode}");
    }

    #endregion
}
