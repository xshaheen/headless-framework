using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.ValueConverters;

[PublicAPI]
public sealed class LocaleValueConverter() : ValueConverter<Locale?, string?>(x => _Serialize(x), x => _Deserialize(x))
{
    private static string _Serialize(Locale? locale)
    {
        return JsonSerializer.Serialize(locale, PlatformJsonConstants.DefaultInternalJsonOptions);
    }

    private static Locale? _Deserialize(string? json)
    {
        return string.IsNullOrEmpty(json) || string.Equals(json, "{}", StringComparison.Ordinal)
            ? null
            : JsonSerializer.Deserialize<Locale>(json, PlatformJsonConstants.DefaultInternalJsonOptions);
    }
}

[PublicAPI]
public sealed class LocaleValueComparer : ValueComparer<Locale?>
{
    public LocaleValueComparer()
        : base(
            equalsExpression: (t1, t2) => _IsEqual(t1, t2),
            hashCodeExpression: t => t == null ? 0 : t.GetHashCode(),
            snapshotExpression: t => t == null ? null : new Locale(t)
        ) { }

    private static bool _IsEqual(Locale? d1, Locale? d2)
    {
        if (d1 == null && d2 == null)
        {
            return true;
        }

        if (d1 == null || d2 == null)
        {
            return false;
        }

        if (d1.Count != d2.Count)
        {
            return false;
        }

        foreach (var pair in d1)
        {
            if (!d2.TryGetValue(pair.Key, out var value))
            {
                return false;
            }

            if (pair.Value.Count != value.Count)
            {
                return false;
            }

            if (!_IsEqual(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool _IsEqual(Dictionary<string, string> map1, Dictionary<string, string> map2)
    {
        if (map1.Count != map2.Count)
        {
            return false;
        }

        foreach (var (key1, value1) in map1)
        {
            if (!map2.TryGetValue(key1, out var value2))
            {
                return false;
            }

            if (!value2.Equals(value1, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
