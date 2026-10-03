// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Represents a phone number as a country calling code plus a national number. Formatting, normalization, region
/// lookup, and parsing need libphonenumber's metadata, so they ship as extension members in the
/// <c>Headless.PhoneNumbers</c> package instead.
/// </summary>
[PublicAPI]
[ComplexType]
public sealed class PhoneNumber : IEquatable<PhoneNumber>
{
    private PhoneNumber() { }

    /// <summary>Initializes a new <see cref="PhoneNumber"/> from a country calling code and national number.</summary>
    /// <param name="countryCode">The country calling code (must be positive).</param>
    /// <param name="number">The national number (must not be null or empty).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="countryCode"/> is not positive.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="number"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="number"/> is empty.</exception>
    public PhoneNumber(int countryCode, string number)
    {
        // The init accessors below own validation and normalization, so object-initializer assignments cannot
        // bypass them either.
        CountryCode = countryCode;
        Number = number;
    }

    /// <summary>The country calling code.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not positive.</exception>
    public int CountryCode
    {
        get;
        init => field = Argument.IsPositive(value);
    }

    /// <summary>
    /// The national (subscriber) number, without the country calling code. Stored in a canonical digits-only
    /// form so that values differing only in formatting (for example <c>"555-1234"</c> and <c>"5551234"</c>)
    /// are equal.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the value is empty.</exception>
    public string Number
    {
        get;
        init => field = _CanonicalizeNumber(Argument.IsNotNullOrEmpty(value));
    } = null!;

    private string? _toStringCache;

    /// <summary>Determines whether this phone number equals <paramref name="other"/> by country code and national number.</summary>
    /// <param name="other">The phone number to compare with.</param>
    /// <returns><see langword="true"/> if both phone numbers are equal; otherwise, <see langword="false"/>.</returns>
    public bool Equals(PhoneNumber? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return CountryCode == other.CountryCode && string.Equals(Number, other.Number, StringComparison.Ordinal);
    }

    /// <summary>Determines whether <paramref name="obj"/> is a <see cref="PhoneNumber"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal phone number; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as PhoneNumber);
    }

    /// <summary>Returns a hash code derived from the country code and national number.</summary>
    /// <returns>A hash code for the current phone number.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(CountryCode, Number);
    }

    /// <summary>Determines whether two phone numbers are equal.</summary>
    /// <param name="left">The first phone number to compare.</param>
    /// <param name="right">The second phone number to compare.</param>
    /// <returns><see langword="true"/> if the phone numbers are equal (including both being <see langword="null"/>); otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(PhoneNumber? left, PhoneNumber? right) => Equals(left, right);

    /// <summary>Determines whether two phone numbers are different.</summary>
    /// <param name="left">The first phone number to compare.</param>
    /// <param name="right">The second phone number to compare.</param>
    /// <returns><see langword="true"/> if the phone numbers differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(PhoneNumber? left, PhoneNumber? right) => !Equals(left, right);

    /// <summary>Returns the number as <c>+{CountryCode} {Number}</c>.</summary>
    /// <remarks>
    /// This is the raw calling code and national number, not a localized format. Use <c>GetInternationalFormat()</c>
    /// from <c>Headless.PhoneNumbers</c> for the formatted international form.
    /// </remarks>
    public override string ToString()
    {
        return _toStringCache ??= $"+{CountryCode.ToString(CultureInfo.InvariantCulture)} {Number}";
    }

    /// <summary>
    /// Reduces a national number to its canonical digits-only form, dropping formatting characters such as
    /// spaces, dashes, parentheses, and a leading <c>+</c> so that equality ignores presentation differences.
    /// </summary>
    /// <param name="number">The national number to canonicalize.</param>
    /// <returns>The digits-only form of <paramref name="number"/>.</returns>
    private static string _CanonicalizeNumber(string number)
    {
        var digitCount = 0;

        foreach (var c in number)
        {
            if (char.IsDigit(c))
            {
                digitCount++;
            }
        }

        // Common case: already digits-only -> return as-is without allocating.
        if (digitCount == number.Length)
        {
            return number;
        }

        return string.Create(
            digitCount,
            number,
            static (span, source) =>
            {
                var i = 0;

                foreach (var c in source)
                {
                    if (char.IsDigit(c))
                    {
                        span[i++] = c;
                    }
                }
            }
        );
    }
}
