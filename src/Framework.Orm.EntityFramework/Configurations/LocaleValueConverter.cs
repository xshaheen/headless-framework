// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Framework.Serializer;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

[PublicAPI]
public sealed class LocalesValueConverter()
    : ValueConverter<Locales?, string?>(x => _Serialize(x), x => _Deserialize(x))
{
    private static string _Serialize(Locales? locale)
    {
        return JsonSerializer.Serialize(locale, JsonConstants.DefaultInternalJsonOptions);
    }

    private static Locales? _Deserialize(string? json)
    {
        return string.IsNullOrEmpty(json) || string.Equals(json, "{}", StringComparison.Ordinal)
            ? null
            : JsonSerializer.Deserialize<Locales>(json, JsonConstants.DefaultInternalJsonOptions);
    }
}

[PublicAPI]
public sealed class LocalesValueComparer : ValueComparer<Locales?>
{
    public LocalesValueComparer()
        : base(
            equalsExpression: (t1, t2) => _IsEqual(t1, t2),
            hashCodeExpression: t => t == null ? 0 : t.GetHashCode(),
            snapshotExpression: t => t == null ? null : new Locales(t)
        ) { }

    private static bool _IsEqual(Locales? d1, Locales? d2)
    {
        if (d1 is null && d2 is null)
        {
            return true;
        }

        if (d1 is null || d2 is null)
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
