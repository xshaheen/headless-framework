namespace Framework.Kernel.BuildingBlocks.Validators;

[PublicAPI]
public static class EgyptianNationalIdValidator
{
    public static readonly FrozenDictionary<string, string> GovernorateIdMap = _CreateGovernorateIdMap();

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

    private static FrozenDictionary<string, string> _CreateGovernorateIdMap()
    {
        var map = new Dictionary<string, string>(StringComparer.InvariantCulture)
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
