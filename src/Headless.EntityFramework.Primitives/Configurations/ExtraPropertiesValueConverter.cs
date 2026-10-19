// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework;

/// <summary>
/// EF Core value converter that serializes <c>ExtraProperties</c> to a JSON string for storage and
/// deserializes it back on read. An empty or <c>{}</c> JSON value round-trips to an empty
/// <c>ExtraProperties</c> instance.
/// </summary>
[PublicAPI]
public sealed class ExtraPropertiesValueConverter()
    : ValueConverter<ExtraProperties, string>(x => _Serialize(x), x => _Deserialize(x))
{
    private static readonly JsonSerializerOptions _Options = EfCoreJsonOptions.Instance;

    private static string _Serialize(ExtraProperties? extraProperties)
    {
        return JsonSerializer.Serialize(extraProperties, _Options);
    }

    private static ExtraProperties _Deserialize(string? json)
    {
        return string.IsNullOrEmpty(json) || string.Equals(json, "{}", StringComparison.Ordinal)
            ? []
            : JsonSerializer.Deserialize<ExtraProperties>(json, _Options) ?? [];
    }
}

/// <summary>
/// EF Core value comparer for <c>ExtraProperties</c>. Two bags are equal when they hold the same keys with values
/// that store the same JSON, regardless of insertion order or boxing, so EF Core saves only bags that actually
/// changed. The snapshot copies the bag, and copies any value that is not a simple immutable value.
/// </summary>
[PublicAPI]
public sealed class ExtraPropertiesValueComparer()
    : ValueComparer<ExtraProperties>(
        equalsExpression: (a, b) => HeadlessJsonValueEquality.AreEqual(a, b),
        hashCodeExpression: dictionary => HeadlessJsonValueEquality.GetHashCode(dictionary),
        snapshotExpression: d => _Snapshot(d)
    )
{
    private static ExtraProperties _Snapshot(ExtraProperties extraProperties)
    {
        var snapshot = new ExtraProperties();

        foreach (var (key, value) in extraProperties)
        {
            snapshot[key] = HeadlessJsonValueEquality.Snapshot(value);
        }

        return snapshot;
    }
}
