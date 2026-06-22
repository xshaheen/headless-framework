// Copyright (c) Mahmoud Shaheen. All rights reserved.

using PhoneNumbers;

namespace Headless.Validators;

/// <summary>Validates mobile phone numbers using Google's libphonenumber.</summary>
[PublicAPI]
public static class MobilePhoneNumberValidator
{
    /// <summary>Determines whether <paramref name="phoneNumber"/> is a possible mobile number for the given <paramref name="countryCode"/>.</summary>
    /// <param name="phoneNumber">The phone number to validate.</param>
    /// <param name="countryCode">The numeric country calling code used to resolve the parsing region.</param>
    /// <returns><see langword="true"/> when the number is a possible mobile number; otherwise <see langword="false"/>. Unparseable or blank input returns <see langword="false"/>.</returns>
    public static bool IsValid(string phoneNumber, int countryCode)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        var util = PhoneNumberUtil.GetInstance();

        PhoneNumber maybePhoneNumber;

        try
        {
            var region = util.GetRegionCodeForCountryCode(countryCode);
            maybePhoneNumber = util.Parse(phoneNumber, region);
        }
        catch
        {
#pragma warning disable ERP022
            return false;
#pragma warning restore ERP022
        }

        return util.IsPossibleNumberForType(maybePhoneNumber, PhoneNumberType.MOBILE);
    }

    /// <summary>Determines whether <paramref name="phoneNumber"/> is a possible mobile number for the given <paramref name="regionCode"/>.</summary>
    /// <param name="phoneNumber">The phone number to validate.</param>
    /// <param name="regionCode">The ISO 3166-1 alpha-2 region code used to parse <paramref name="phoneNumber"/>.</param>
    /// <returns><see langword="true"/> when the number is a possible mobile number; otherwise <see langword="false"/>. Unparseable or blank input returns <see langword="false"/>.</returns>
    public static bool IsValid(string phoneNumber, string regionCode)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        var util = PhoneNumberUtil.GetInstance();

        PhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = util.Parse(phoneNumber, regionCode);
        }
        catch
        {
#pragma warning disable ERP022
            return false;
#pragma warning restore ERP022
        }

        return util.IsPossibleNumberForType(maybePhoneNumber, PhoneNumberType.MOBILE);
    }

    /// <summary>
    /// Determines whether <paramref name="internationalNumber"/> is a possible mobile number in E.164 international
    /// form and, when valid, returns its normalized <c>+&lt;countryCode&gt;&lt;nationalNumber&gt;</c> representation.
    /// </summary>
    /// <param name="internationalNumber">The international phone number, which must start with <c>+</c>.</param>
    /// <param name="normalizedNumber">When this method returns <see langword="true"/>, the normalized E.164 number; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the number is a possible mobile number; otherwise <see langword="false"/>. Unparseable or blank input returns <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="internationalNumber"/> is non-blank but does not start with <c>+</c>.</exception>
    public static bool IsValid(string? internationalNumber, [NotNullWhen(true)] out string? normalizedNumber)
    {
        normalizedNumber = null;

        if (string.IsNullOrWhiteSpace(internationalNumber))
        {
            return false;
        }

        if (!internationalNumber.StartsWith('+'))
        {
            throw new ArgumentException("International phone number must start with '+'", nameof(internationalNumber));
        }

        var util = PhoneNumberUtil.GetInstance();

        PhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = util.Parse(internationalNumber, defaultRegion: null);
        }
        catch
        {
#pragma warning disable ERP022
            return false;
#pragma warning restore ERP022
        }

        if (!util.IsPossibleNumberForType(maybePhoneNumber, PhoneNumberType.MOBILE))
        {
            return false;
        }

        FormattableString normalized = $"+{maybePhoneNumber.CountryCode}{maybePhoneNumber.NationalNumber}";

        normalizedNumber = normalized.ToString(CultureInfo.InvariantCulture);

        return true;
    }
}
