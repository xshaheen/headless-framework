using System.Diagnostics.CodeAnalysis;
using PhoneNumbers;

namespace Framework.BuildingBlocks.Validators;

#pragma warning disable ERP022 // Exit point 'return result;' swallows an unobserved exception.
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
            return false;
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
            return false;
        }

        return util.IsPossibleNumberForType(maybePhoneNumber, PhoneNumberType.MOBILE);
    }

    public static bool IsValid(string internationalPhoneNumber, [NotNullWhen(true)] out string? normalizedPhoneNumber)
    {
        normalizedPhoneNumber = null;

        if (string.IsNullOrWhiteSpace(internationalPhoneNumber))
        {
            return false;
        }

        var util = PhoneNumberUtil.GetInstance();

        PhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = util.Parse(internationalPhoneNumber.EnsureStartsWith('+'), defaultRegion: null);
        }
        catch
        {
            return false;
        }

        if (!util.IsPossibleNumberForType(maybePhoneNumber, PhoneNumberType.MOBILE))
        {
            return false;
        }

        FormattableString normalized = $"+${maybePhoneNumber.CountryCode}{maybePhoneNumber.NationalNumber}";

        normalizedPhoneNumber = normalized.ToString(CultureInfo.InvariantCulture);

        return true;
    }
}
