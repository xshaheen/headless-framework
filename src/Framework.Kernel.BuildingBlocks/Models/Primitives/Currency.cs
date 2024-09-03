using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Unicode;
using Framework.Kernel.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
[ComplexType]
[DebuggerDisplay("{" + nameof(Amount) + "}{" + nameof(CurrencyCode) + "}")]
public sealed class Currency(decimal amount, string currencyCode)
    : IComparable,
        IComparable<Currency>,
        IComparable<decimal>,
        IAdditionOperators<Currency, Currency, Currency>,
        ISubtractionOperators<Currency, Currency, Currency>,
        IMultiplyOperators<Currency, Currency, Currency>,
        IDivisionOperators<Currency, Currency, Currency>,
        IModulusOperators<Currency, Currency, Currency>,
        IComparisonOperators<Currency, Currency, bool>,
        ISpanParsable<Currency?>,
        IUtf8SpanFormattable,
        ISpanFormattable,
        IEquatable<Currency?>
{
    #region Static

    public static readonly Currency ZeroEgp = new(0, "EGP");
    public static readonly Currency ZeroUsd = new(0, "USD");

    #endregion

    #region Props

    public decimal Amount { get; private init; } = amount;

    public string CurrencyCode { get; private init; } = Argument.IsNotNullOrWhiteSpace(currencyCode);

    private FormattableString Format => $"{Amount}{CurrencyCode}";

    #endregion

    #region IEquatable Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Currency other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Currency? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(CurrencyCode, other.CurrencyCode, StringComparison.Ordinal) && other.Amount == Amount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Currency? left, Currency? right) => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Currency? left, Currency? right) => !(left == right);

    #endregion

    #region IComparable Implementation

    public int CompareTo(object? obj)
    {
        return obj switch
        {
            null => 1,
            decimal d => CompareTo(d),
            Currency c => CompareTo(c),
            _ => throw new ArgumentException($"Object is not a {nameof(Currency)}", nameof(obj)),
        };
    }

    public int CompareTo(Currency? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (!string.Equals(CurrencyCode, other.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot compare currencies with different currency codes");
        }

        return Amount.CompareTo(other.Amount);
    }

    public int CompareTo(decimal other) => Amount.CompareTo(other);

    #endregion

    #region IParsable Implementation

    public static Currency Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse(string? s, IFormatProvider? provider, [NotNullWhen(true)] out Currency? result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static Currency Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out var result) ? result : throw new FormatException("Invalid currency format");
    }

    public static bool TryParse(
        ReadOnlySpan<char> s,
        IFormatProvider? provider,
        [NotNullWhen(true)] out Currency? result
    )
    {
        // 1. Find the index the end of amount
        var lastDigitIndex = 0;

        for (var i = 0; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i]))
            {
                continue;
            }

            lastDigitIndex = i;

            break;
        }

        // 2. Parse the amount
        var amountSpan = s[..lastDigitIndex];
        var currencyCodeSpan = s[lastDigitIndex..];

        if (amountSpan.IsEmpty)
        {
            result = null!;
            return false;
        }

        if (!decimal.TryParse(amountSpan, NumberStyles.Any, provider, out var amount))
        {
            result = null;

            return false;
        }

        // 3. Parse the currency code
        var currencyCode = currencyCodeSpan.ToString();

        result = new Currency(amount, currencyCode);

        return true;
    }

    #endregion

    #region ISpanFormattable & IUtf8SpanFormattable Implementation

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider
    )
    {
        return destination.TryWrite(provider, $"{Amount}{CurrencyCode}", out charsWritten);
    }

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

    public static Currency operator +(Currency left, Currency right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot add currencies with different currency codes");
        }

        return new Currency(left.Amount + right.Amount, left.CurrencyCode);
    }

    public static Currency Add(Currency left, Currency right) => left + right;

    public static Currency operator -(Currency left, Currency right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot subtract currencies with different currency codes");
        }

        return new Currency(left.Amount - right.Amount, left.CurrencyCode);
    }

    public static Currency Subtract(Currency left, Currency right) => left - right;

    public static Currency operator *(Currency left, Currency right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot multiply currencies with different currency codes");
        }

        return new Currency(left.Amount * right.Amount, left.CurrencyCode);
    }

    public static Currency Multiply(Currency left, Currency right) => left * right;

    public static Currency operator %(Currency left, Currency right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot get modulus of currencies with different currency codes");
        }

        return new Currency(left.Amount % right.Amount, left.CurrencyCode);
    }

    public static Currency Mod(Currency left, Currency right) => left % right;

    public static Currency operator /(Currency left, Currency right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot divide currencies with different currency codes");
        }

        return new Currency(left.Amount / right.Amount, left.CurrencyCode);
    }

    public static Currency Divide(Currency left, Currency right) => left / right;

    #endregion

    #region Comparison Operators

    public static bool operator >(Currency left, Currency right) => left.CompareTo(right) > 0;

    public static bool operator >=(Currency left, Currency right) => left.CompareTo(right) >= 0;

    public static bool operator <(Currency left, Currency right) => left.CompareTo(right) < 0;

    public static bool operator <=(Currency left, Currency right) => left.CompareTo(right) <= 0;

    public static bool operator >(decimal left, Currency right) => left > right.Amount;

    public static bool operator <(decimal left, Currency right) => left < right.Amount;

    public static bool operator >=(decimal left, Currency right) => left >= right.Amount;

    public static bool operator <=(decimal left, Currency right) => left <= right.Amount;

    public static bool operator >(Currency left, decimal right) => left.Amount > right;

    public static bool operator <(Currency left, decimal right) => left.Amount < right;

    public static bool operator >=(Currency left, decimal right) => left.Amount >= right;

    public static bool operator <=(Currency left, decimal right) => left.Amount <= right;

    #endregion

    #region Basic Methods

    public override int GetHashCode() => HashCode.Combine(Amount, CurrencyCode);

    public override string ToString() => Format.ToString(CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) => Format.ToString(formatProvider);

    #endregion
}
