// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// EF Core value comparer for nullable <c>Locales</c> that performs deep structural equality
/// so EF Core can accurately detect changes and avoid spurious column updates.
/// </summary>
/// <remarks>
/// The snapshot copies every inner dictionary as well as the outer one; a shared inner dictionary would absorb an
/// in-place edit such as <c>locales["en"]["name"] = value</c> and hide it from change detection.
/// </remarks>
[PublicAPI]
public sealed class LocalesValueComparer()
    : ValueComparer<Locales?>(
        equalsExpression: (t1, t2) => _IsEqual(t1, t2),
        hashCodeExpression: t => t == null ? 0 : t.Count,
        snapshotExpression: t => _Snapshot(t)
    )
{
    private static Locales? _Snapshot(Locales? locales)
    {
        if (locales is null)
        {
            return null;
        }

        var snapshot = new Locales();

        foreach (var (culture, values) in locales)
        {
            snapshot[culture] = new Dictionary<string, string>(values, values.Comparer);
        }

        return snapshot;
    }

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
