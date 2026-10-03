// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;
using NumberParseException = global::PhoneNumbers.NumberParseException;
using PhoneNumber = Headless.Primitives.PhoneNumber;
using PhoneNumberFormat = global::PhoneNumbers.PhoneNumberFormat;
using PhoneNumberUtil = global::PhoneNumbers.PhoneNumberUtil;
using UtilsPhoneNumber = global::PhoneNumbers.PhoneNumber;

namespace Headless.PhoneNumbers;

/// <summary>
/// libphonenumber-backed formatting, normalization, region lookup, and parsing for the
/// <see cref="PhoneNumber"/> value object.
/// </summary>
/// <remarks>
/// <see cref="PhoneNumber"/> itself carries only the country calling code and the national number, so the
/// foundation packages that hold it do not depend on libphonenumber. Every member here parses the number on each
/// call; cache the result when a hot path formats the same number repeatedly.
/// </remarks>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class HeadlessPhoneNumberExtensions
{
    extension(PhoneNumber phoneNumber)
    {
        /// <summary>Formats this phone number in its national format.</summary>
        /// <returns>The national-format string, or <see langword="null"/> when the number cannot be parsed or is not a possible number.</returns>
        public string? GetNationalFormat()
        {
            return PhoneNumber.GetNationalFormat(_Concatenate(phoneNumber));
        }

        /// <summary>Formats this phone number in its international format.</summary>
        /// <returns>The international-format string, or <see langword="null"/> when the number cannot be parsed or is not a possible number.</returns>
        public string? GetInternationalFormat()
        {
            return PhoneNumber.GetInternationalFormat(_Concatenate(phoneNumber));
        }

        /// <summary>Returns the region a phone number belongs to, usable for geocoding at the region level.</summary>
        /// <returns>The ISO 3166-1 alpha-2 region code, or <see langword="null"/> when no region matches the calling code.</returns>
        /// <exception cref="NumberParseException">Thrown when the number cannot be parsed by libphonenumber.</exception>
        public string? GetRegionCodes()
        {
            return PhoneNumberUtil.GetInstance().GetRegionCodeForNumber(phoneNumber.ToUtilsPhoneNumber());
        }

        /// <summary>
        /// Returns the normalized canonical form of this phone number: its international format, or
        /// <c>+{CountryCode} {Number}</c> when it cannot be formatted, passed through
        /// <see cref="LookupNormalizer.NormalizePhoneNumber(string?)"/>.
        /// </summary>
        /// <returns>The normalized phone number string.</returns>
        public string Normalize()
        {
            return LookupNormalizer.NormalizePhoneNumber(
                phoneNumber.GetInternationalFormat() ?? phoneNumber.ToString()
            );
        }

        /// <summary>Converts this instance to libphonenumber's <see cref="UtilsPhoneNumber"/> by parsing its concatenated form.</summary>
        /// <returns>The parsed libphonenumber phone number.</returns>
        /// <exception cref="NumberParseException">Thrown when the number cannot be parsed by libphonenumber.</exception>
        public UtilsPhoneNumber ToUtilsPhoneNumber()
        {
            return PhoneNumberUtil.GetInstance().Parse(_Concatenate(phoneNumber), defaultRegion: null);
        }
    }

    extension(PhoneNumber)
    {
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

            return PhoneNumber.FromPhoneNumber(PhoneNumberUtil.GetInstance().Parse(number, defaultRegion: null));
        }

        /// <summary>Creates a <see cref="PhoneNumber"/> from libphonenumber's <see cref="UtilsPhoneNumber"/>.</summary>
        /// <param name="number">The libphonenumber phone number to convert; may be <see langword="null"/>.</param>
        /// <returns>The converted <see cref="PhoneNumber"/>, or <see langword="null"/> when <paramref name="number"/> is <see langword="null"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the source country code is not positive.</exception>
        /// <exception cref="ArgumentException">Thrown when the source national number is empty.</exception>
        [return: NotNullIfNotNull(nameof(number))]
        public static PhoneNumber? FromPhoneNumber(UtilsPhoneNumber? number)
        {
            return number is null
                ? null
                : new PhoneNumber(number.CountryCode, number.NationalNumber.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Parses an international-format number and returns its normalized canonical form.</summary>
        /// <param name="number">The phone number in international format.</param>
        /// <returns>The normalized phone number string.</returns>
        /// <exception cref="NumberParseException">Thrown when <paramref name="number"/> cannot be parsed by libphonenumber.</exception>
        public static string NormalizeInternationalNumber(string number)
        {
            return PhoneNumber.FromInternationalFormat(number).Normalize();
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
            var concatenated = $"+{code.ToString(CultureInfo.InvariantCulture)}{number}";

            return LookupNormalizer.NormalizePhoneNumber(
                PhoneNumber.GetInternationalFormat(concatenated) ?? concatenated
            );
        }

        /// <summary>Formats the given phone number string in its national format.</summary>
        /// <param name="phoneNumber">The phone number to format; may be <see langword="null"/>.</param>
        /// <returns>
        /// The national-format string, or <see langword="null"/> when <paramref name="phoneNumber"/> is <see langword="null"/>,
        /// cannot be parsed, or is not a possible number.
        /// </returns>
        public static string? GetNationalFormat(string? phoneNumber)
        {
            if (phoneNumber is null || !_TryParse(phoneNumber, out var util, out var parsed))
            {
                return null;
            }

            return util.IsPossibleNumberWithReason(parsed) switch
            {
                PhoneNumberUtil.ValidationResult.IS_POSSIBLE
                or PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY => util.Format(
                    parsed,
                    PhoneNumberFormat.NATIONAL
                ),
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
            if (!_TryParse(phoneNumber, out var util, out var parsed))
            {
                return null;
            }

            return util.IsPossibleNumberWithReason(parsed) switch
            {
                PhoneNumberUtil.ValidationResult.IS_POSSIBLE => util.Format(parsed, PhoneNumberFormat.INTERNATIONAL),
                _ => null,
            };
        }
    }

    private static string _Concatenate(PhoneNumber phoneNumber)
    {
        return $"+{phoneNumber.CountryCode.ToString(CultureInfo.InvariantCulture)}{phoneNumber.Number}";
    }

    private static bool _TryParse(
        string? phoneNumber,
        out PhoneNumberUtil util,
        [NotNullWhen(true)] out UtilsPhoneNumber? parsed
    )
    {
        util = PhoneNumberUtil.GetInstance();

        try
        {
            parsed = util.Parse(numberToParse: phoneNumber, defaultRegion: null);

            return true;
        }
        catch (NumberParseException)
        {
            parsed = null;

            return false;
        }
    }
}
