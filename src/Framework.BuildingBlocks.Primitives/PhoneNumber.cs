using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Framework.BuildingBlocks.Domains;
using Framework.BuildingBlocks.Extensions.Normalizers;
using PhoneNumbers;
using UtilsPhoneNumber = PhoneNumbers.PhoneNumber;

namespace Framework.BuildingBlocks.Primitives;

[PublicAPI]
[ComplexType]
public sealed class PhoneNumber : ValueObject
{
    private PhoneNumber() { }

    public PhoneNumber(int countryCode, string number)
    {
        CountryCode = countryCode;
        Number = Normalize(number);
    }

    public int CountryCode { get; init; }

    public string Number { get; init; } = null!;

    public string? GetNationalFormat()
    {
        var util = PhoneNumberUtil.GetInstance();

        UtilsPhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = util.Parse(numberToParse: Normalize(), defaultRegion: null);
        }
        catch (NumberParseException)
        {
            return null;
        }

        var validationResult = util.IsPossibleNumberWithReason(maybePhoneNumber);

        return validationResult switch
        {
            PhoneNumberUtil.ValidationResult.IS_POSSIBLE
            or PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY
                => util.Format(maybePhoneNumber, PhoneNumberFormat.INTERNATIONAL),
            _ => null,
        };
    }

    public string? GetInternationalFormat()
    {
        var util = PhoneNumberUtil.GetInstance();

        UtilsPhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = util.Parse(numberToParse: Normalize(), defaultRegion: null);
        }
        catch (NumberParseException)
        {
            return null;
        }

        var validationResult = util.IsPossibleNumberWithReason(maybePhoneNumber);

        return validationResult switch
        {
            PhoneNumberUtil.ValidationResult.IS_POSSIBLE
                => util.Format(maybePhoneNumber, PhoneNumberFormat.INTERNATIONAL),
            _ => null,
        };
    }

    /// <summary>Returns the region where a phone number is from. This could be used for geocoding at the region level.</summary>
    /// <returns>The region where the phone number is from, or null if no region matches this calling code.</returns>
    public string? GetRegionCodes()
    {
        var phoneNumber = ToUtilsPhoneNumber();

        return PhoneNumberUtil.GetInstance().GetRegionCodeForNumber(phoneNumber);
    }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return CountryCode;
        yield return Number;
    }

    public override string ToString() => $"({CountryCode}) {Number}";

    public string Normalize() => Normalize(CountryCode, Number);

    public static string Normalize(int code, string number) =>
        $"+{code.ToString(CultureInfo.InvariantCulture)}{Normalize(number)}";

    public static string Normalize(string number) => number.NormalizePhoneNumber();

    public UtilsPhoneNumber ToUtilsPhoneNumber()
    {
        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        var numberToParse = Normalize();
        var phoneNumber = phoneNumberUtil.Parse(numberToParse, defaultRegion: null);

        return phoneNumber;
    }

    [return: NotNullIfNotNull(nameof(operand))]
    public static PhoneNumber? FromPhoneNumber(UtilsPhoneNumber? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PhoneNumber?(UtilsPhoneNumber? operand)
    {
        return operand is null
            ? null
            : new(operand.CountryCode, operand.NationalNumber.ToString(CultureInfo.InvariantCulture));
    }

    public static PhoneNumber FromInternationalFormat(string number)
    {
        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        var phoneNumber = phoneNumberUtil.Parse(number, defaultRegion: null);

        return new(phoneNumber.CountryCode, phoneNumber.NationalNumber.ToString(CultureInfo.InvariantCulture));
    }
}
