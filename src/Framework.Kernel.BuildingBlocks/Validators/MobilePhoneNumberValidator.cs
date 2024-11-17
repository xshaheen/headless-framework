// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using PhoneNumbers;

namespace Framework.Kernel.BuildingBlocks.Validators;

[PublicAPI]
public static class MobilePhoneNumberValidator
{
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

    public static bool IsValid(string? internationalNumber, [NotNullWhen(true)] out string? normalizedNumber)
    {
        normalizedNumber = null;

        if (string.IsNullOrWhiteSpace(internationalNumber))
        {
            return false;
        }

        if (!internationalNumber.StartsWith('+'))
        {
            throw new ArgumentException(
                "International phone number should not start with +",
                nameof(internationalNumber)
            );
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
