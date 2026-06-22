// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Validators;

/// <summary>Validates and parses 14-digit Egyptian national identity numbers.</summary>
[PublicAPI]
public static class EgyptianNationalIdValidator
{
    /// <summary>
    /// Maps the two-digit governorate code embedded in a national ID (digits 8-9) to the Arabic governorate name.
    /// </summary>
    public static readonly FrozenDictionary<string, string> GovernorateIdMap = _CreateGovernorateIdMap();

    /// <summary>Determines whether <paramref name="nationalId"/> is a structurally valid Egyptian national ID.</summary>
    /// <param name="nationalId">The national ID string to validate.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="nationalId"/> is 14 digits encoding a valid birth date and a known
    /// governorate code; otherwise <see langword="false"/>. Invalid or <see langword="null"/>/empty input returns
    /// <see langword="false"/> rather than throwing.
    /// </returns>
    public static bool IsValid(string nationalId)
    {
        if (string.IsNullOrEmpty(nationalId))
        {
            return false;
        }

        if (nationalId.Length != 14)
        {
            return false;
        }

        if (!nationalId.All(char.IsDigit))
        {
            return false;
        }

        if (
            !int.TryParse(
                nationalId[..1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var yearCenturyIndicator
            )
        )
        {
            return false;
        }

        if (!int.TryParse(nationalId[1..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var yearNumber))
        {
            return false;
        }

        var year = ((17 + yearCenturyIndicator) * 100) + yearNumber;

        if (!int.TryParse(nationalId[3..5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
        {
            return false;
        }

        if (!int.TryParse(nationalId[5..7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return false;
        }

        try
        {
            _ = new DateOnly(year, month, day);
        }
        catch
        {
#pragma warning disable ERP022 // ERP022: Exit point 'return false;' swallows an unobserved exception
            return false;
#pragma warning restore ERP022
        }

        var governorateKey = nationalId[7..9];

        return GovernorateIdMap.ContainsKey(governorateKey);
    }

    /// <summary>Validates <paramref name="nationalId"/> and, when valid, extracts the encoded birth date and governorate.</summary>
    /// <param name="nationalId">The national ID string to parse.</param>
    /// <param name="year">When this method returns <see langword="true"/>, the encoded birth year; otherwise <c>0</c>.</param>
    /// <param name="month">When this method returns <see langword="true"/>, the encoded birth month; otherwise <c>0</c>.</param>
    /// <param name="day">When this method returns <see langword="true"/>, the encoded birth day; otherwise <c>0</c>.</param>
    /// <param name="governorateName">When this method returns <see langword="true"/>, the Arabic governorate name; otherwise <see cref="string.Empty"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="nationalId"/> is valid and was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string nationalId, out int year, out int month, out int day, out string governorateName)
    {
        if (!IsValid(nationalId))
        {
            year = 0;
            month = 0;
            day = 0;
            governorateName = string.Empty;

            return false;
        }

        var yearCenturyIndicator = int.Parse(nationalId[..1], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var yearNumber = int.Parse(nationalId[1..3], NumberStyles.Integer, CultureInfo.InvariantCulture);

        year = ((17 + yearCenturyIndicator) * 100) + yearNumber;
        month = int.Parse(nationalId[3..5], NumberStyles.Integer, CultureInfo.InvariantCulture);
        day = int.Parse(nationalId[5..7], NumberStyles.Integer, CultureInfo.InvariantCulture);
        governorateName = GovernorateIdMap[nationalId[7..9]];

        return true;
    }

    private static FrozenDictionary<string, string> _CreateGovernorateIdMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["01"] = "القاهرة",
            ["02"] = "آلإسكندرية",
            ["03"] = "بور سعيد",
            ["04"] = "السويس",
            ["11"] = "دمياط",
            ["12"] = "الدقهلية",
            ["13"] = "الشرقية",
            ["14"] = "القليوبية",
            ["15"] = "كفر الشيخ",
            ["16"] = "الغربية",
            ["17"] = "المنوفية",
            ["18"] = "البحيرة",
            ["19"] = "الإسماعيلية",
            ["21"] = "الجيزة",
            ["22"] = "بني سويف",
            ["23"] = "الفيوم",
            ["24"] = "المنيا",
            ["25"] = "أسيوط",
            ["27"] = "قنا",
            ["28"] = "أسوان",
            ["29"] = "الأقصر",
            ["31"] = "البحر الأحمر",
            ["32"] = "الوادي الجديد",
            ["33"] = "مطروح",
            ["34"] = "شمال سيناء",
            ["35"] = "جنوب سيناء",
            ["88"] = "N/A",
        };

        return map.ToFrozenDictionary();
    }
}
