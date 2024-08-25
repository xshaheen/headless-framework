using System.Diagnostics.CodeAnalysis;
using PhoneNumbers;

namespace Framework.BuildingBlocks.Validators;

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
#pragma warning disable ERP022
            return false;
#pragma warning restore ERP022
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
