// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Domain;
using Headless.Text;
using PhoneNumbers;
using UtilsPhoneNumber = PhoneNumbers.PhoneNumber;

namespace Headless.Primitives;

[PublicAPI]
[ComplexType]
public sealed class PhoneNumber : ValueObject
{
    private PhoneNumber() { }

    public PhoneNumber(int countryCode, string number)
    {
        CountryCode = Argument.IsPositive(countryCode);
        Number = Argument.IsNotNullOrEmpty(number);
    }

    public int CountryCode { get; init; }

    public string Number { get; init; } = null!;

    private string ConcatenatedNumber => $"+{CountryCode.ToString(CultureInfo.InvariantCulture)}{Number}";

    public string? GetNationalFormat() => GetNationalFormat(ConcatenatedNumber);

    public string? GetInternationalFormat() => GetInternationalFormat(ConcatenatedNumber);

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

    public override string ToString() => GetInternationalFormat() ?? $"+{CountryCode} {Number}";

    public string Normalize() => LookupNormalizer.NormalizePhoneNumber(ToString());

    #region Utils

    public static string NormalizeInternationalNumber(string number)
    {
        var phoneNumber = FromInternationalFormat(number);

        return phoneNumber.Normalize();
    }

    public static string Normalize(int code, string number)
    {
        var phoneNumber = $"+{code.ToString(CultureInfo.InvariantCulture)}{number}";
        var international = GetInternationalFormat(phoneNumber);

        return LookupNormalizer.NormalizePhoneNumber(international ?? phoneNumber);
    }

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

    public UtilsPhoneNumber ToUtilsPhoneNumber()
    {
        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        var phoneNumber = phoneNumberUtil.Parse(ConcatenatedNumber, defaultRegion: null);

        return phoneNumber;
    }

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator UtilsPhoneNumber?(PhoneNumber? operand)
    {
        return operand?.ToUtilsPhoneNumber();
    }

    #endregion

    #region Factories

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

    [return: NotNullIfNotNull(nameof(number))]
    public static PhoneNumber? FromPhoneNumber(UtilsPhoneNumber? number) => number;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PhoneNumber?(UtilsPhoneNumber? operand)
    {
        return operand is null
            ? null
            : new(operand.CountryCode, operand.NationalNumber.ToString(CultureInfo.InvariantCulture));
    }

    #endregion
}
