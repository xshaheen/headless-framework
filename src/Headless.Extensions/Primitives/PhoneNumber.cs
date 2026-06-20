// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using Headless.Checks;
using Headless.Text;
using PhoneNumbers;
using UtilsPhoneNumber = PhoneNumbers.PhoneNumber;

namespace Headless.Primitives;

/// <summary>
/// Represents a phone number as a country calling code plus a national number, with helpers to format, normalize,
/// and convert it using the underlying libphonenumber utilities.
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
        CountryCode = Argument.IsPositive(countryCode);
        Number = Argument.IsNotNullOrEmpty(number);
    }

    /// <summary>The country calling code.</summary>
    public int CountryCode { get; init; }

    /// <summary>The national (subscriber) number, without the country calling code.</summary>
    public string Number { get; init; } = null!;

    private string ConcatenatedNumber => $"+{CountryCode.ToString(CultureInfo.InvariantCulture)}{Number}";

    /// <summary>Formats this phone number in its national format.</summary>
    /// <returns>The national-format string, or <see langword="null"/> when the number cannot be parsed or is not a possible number.</returns>
    public string? GetNationalFormat() => GetNationalFormat(ConcatenatedNumber);

    /// <summary>Formats this phone number in its international format.</summary>
    /// <returns>The international-format string, or <see langword="null"/> when the number cannot be parsed or is not a possible number.</returns>
    public string? GetInternationalFormat() => GetInternationalFormat(ConcatenatedNumber);

    /// <summary>Returns the region where a phone number is from. This could be used for geocoding at the region level.</summary>
    /// <returns>The region where the phone number is from, or null if no region matches this calling code.</returns>
    /// <exception cref="NumberParseException">Thrown when the concatenated number cannot be parsed by libphonenumber.</exception>
    public string? GetRegionCodes()
    {
        var phoneNumber = ToUtilsPhoneNumber();

        return PhoneNumberUtil.GetInstance().GetRegionCodeForNumber(phoneNumber);
    }

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
    public override bool Equals(object? obj) => Equals(obj as PhoneNumber);

    /// <summary>Returns a hash code derived from the country code and national number.</summary>
    /// <returns>A hash code for the current phone number.</returns>
    public override int GetHashCode() => HashCode.Combine(CountryCode, Number);

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

    /// <summary>Returns the international format of the number, falling back to <c>+{CountryCode} {Number}</c> when it cannot be formatted.</summary>
    public override string ToString() => GetInternationalFormat() ?? $"+{CountryCode} {Number}";

    /// <summary>Returns a normalized canonical representation of this phone number derived from its string form.</summary>
    /// <returns>The normalized phone number string.</returns>
    public string Normalize() => LookupNormalizer.NormalizePhoneNumber(ToString());

    #region Utils

    /// <summary>Parses an international-format number and returns its normalized canonical representation.</summary>
    /// <param name="number">The phone number in international format.</param>
    /// <returns>The normalized phone number string.</returns>
    /// <exception cref="NumberParseException">Thrown when <paramref name="number"/> cannot be parsed by libphonenumber.</exception>
    public static string NormalizeInternationalNumber(string number)
    {
        var phoneNumber = FromInternationalFormat(number);

        return phoneNumber.Normalize();
    }

    /// <summary>Normalizes a phone number given its country calling code and national number.</summary>
    /// <param name="code">The country calling code.</param>
    /// <param name="number">The national number.</param>
    /// <returns>
    /// The normalized phone number string; when the number cannot be formatted internationally, the raw concatenated
    /// number is normalized instead.
    /// </returns>
    public static string Normalize(int code, string number)
    {
        var phoneNumber = $"+{code.ToString(CultureInfo.InvariantCulture)}{number}";
        var international = GetInternationalFormat(phoneNumber);

        return LookupNormalizer.NormalizePhoneNumber(international ?? phoneNumber);
    }

    /// <summary>Formats the given phone number string in its national format.</summary>
    /// <param name="phoneNumber">The phone number to format; may be <see langword="null"/>.</param>
    /// <returns>
    /// The national-format string, or <see langword="null"/> when <paramref name="phoneNumber"/> is <see langword="null"/>,
    /// cannot be parsed, or is not a possible number.
    /// </returns>
    public static string? GetNationalFormat(string? phoneNumber)
    {
        if (phoneNumber is null)
        {
            return null;
        }

        var util = PhoneNumberUtil.GetInstance();

        UtilsPhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = util.Parse(numberToParse: phoneNumber, defaultRegion: null);
        }
        catch (NumberParseException)
        {
            return null;
        }

        var validationResult = util.IsPossibleNumberWithReason(maybePhoneNumber);

        return validationResult switch
        {
            PhoneNumberUtil.ValidationResult.IS_POSSIBLE or PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY =>
                util.Format(maybePhoneNumber, PhoneNumberFormat.NATIONAL),
            _ => null,
        };
    }

    /// <summary>Formats the given phone number string in its international format.</summary>
    /// <param name="phoneNumber">The phone number to format; may be <see langword="null"/>.</param>
    /// <returns>
    /// The international-format string, or <see langword="null"/> when <paramref name="phoneNumber"/> cannot be parsed
    /// or is not a possible number.
    /// </returns>
    public static string? GetInternationalFormat(string? phoneNumber)
    {
        var util = PhoneNumberUtil.GetInstance();

        UtilsPhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = util.Parse(numberToParse: phoneNumber, defaultRegion: null);
        }
        catch (NumberParseException)
        {
            return null;
        }

        var validationResult = util.IsPossibleNumberWithReason(maybePhoneNumber);

        return validationResult switch
        {
            PhoneNumberUtil.ValidationResult.IS_POSSIBLE => util.Format(
                maybePhoneNumber,
                PhoneNumberFormat.INTERNATIONAL
            ),
            _ => null,
        };
    }

    #endregion

    #region To Utils Phone Number

    /// <summary>Converts this instance to the underlying libphonenumber <see cref="UtilsPhoneNumber"/> by parsing its concatenated form.</summary>
    /// <returns>The parsed libphonenumber phone number.</returns>
    /// <exception cref="NumberParseException">Thrown when the concatenated number cannot be parsed by libphonenumber.</exception>
    public UtilsPhoneNumber ToUtilsPhoneNumber()
    {
        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        var phoneNumber = phoneNumberUtil.Parse(ConcatenatedNumber, defaultRegion: null);

        return phoneNumber;
    }

    /// <summary>Implicitly converts a <see cref="PhoneNumber"/> to the underlying libphonenumber <see cref="UtilsPhoneNumber"/>.</summary>
    /// <param name="operand">The phone number to convert; may be <see langword="null"/>.</param>
    /// <returns>The parsed libphonenumber phone number, or <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.</returns>
    /// <exception cref="NumberParseException">Thrown when the concatenated number cannot be parsed by libphonenumber.</exception>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator UtilsPhoneNumber?(PhoneNumber? operand)
    {
        return operand?.ToUtilsPhoneNumber();
    }

    #endregion

    #region Factories

    /// <summary>Creates a <see cref="PhoneNumber"/> by parsing an international-format string.</summary>
    /// <param name="number">The phone number in international format; may be <see langword="null"/>.</param>
    /// <returns>The parsed <see cref="PhoneNumber"/>, or <see langword="null"/> when <paramref name="number"/> is <see langword="null"/>.</returns>
    /// <exception cref="NumberParseException">Thrown when <paramref name="number"/> cannot be parsed by libphonenumber.</exception>
    [return: NotNullIfNotNull(nameof(number))]
    public static PhoneNumber? FromInternationalFormat(string? number)
    {
        if (number is null)
        {
            return null;
        }

        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        var phoneNumber = phoneNumberUtil.Parse(number, defaultRegion: null);

        return new(phoneNumber.CountryCode, phoneNumber.NationalNumber.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Creates a <see cref="PhoneNumber"/> from a libphonenumber <see cref="UtilsPhoneNumber"/>.</summary>
    /// <param name="number">The libphonenumber phone number to convert; may be <see langword="null"/>.</param>
    /// <returns>The converted <see cref="PhoneNumber"/>, or <see langword="null"/> when <paramref name="number"/> is <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the source country code is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when the source national number is empty.</exception>
    [return: NotNullIfNotNull(nameof(number))]
    public static PhoneNumber? FromPhoneNumber(UtilsPhoneNumber? number) => number;

    /// <summary>Implicitly converts a libphonenumber <see cref="UtilsPhoneNumber"/> to a <see cref="PhoneNumber"/>.</summary>
    /// <param name="operand">The libphonenumber phone number to convert; may be <see langword="null"/>.</param>
    /// <returns>The converted <see cref="PhoneNumber"/>, or <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="operand"/>'s country code is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="operand"/>'s national number is empty.</exception>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PhoneNumber?(UtilsPhoneNumber? operand)
    {
        return operand is null
            ? null
            : new(operand.CountryCode, operand.NationalNumber.ToString(CultureInfo.InvariantCulture));
    }

    #endregion
}
