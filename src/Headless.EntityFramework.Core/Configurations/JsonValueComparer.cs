// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// Value comparer for a property stored as JSON text. Two values are equal when they serialize to the same JSON,
/// and the change-tracking snapshot is a deep copy made by a JSON round trip.
/// </summary>
/// <remarks>
/// Pair it with any JSON value converter on a mutable reference type (a collection, a dictionary, or an object
/// graph). Without a comparer EF Core snapshots the property's reference, so an in-place mutation such as
/// <c>entity.Values["key"] = value</c> changes the snapshot too and the update is never saved. Comparing the
/// serialized form asks the question that matters for a JSON column: would the stored text change.
/// </remarks>
/// <typeparam name="T">The property type.</typeparam>
/// <param name="options">
/// Serializer options; pass the options of the property's value converter so equality matches the stored text.
/// </param>
[PublicAPI]
public sealed class JsonValueComparer<T>(JsonSerializerOptions options)
    : ValueComparer<T>(
        (left, right) => _AreEqual(left, right, options),
        value => _GetHashCode(value, options),
        value => _Snapshot(value, options)
    )
{
    /// <summary>Creates a comparer that serializes with the options the Headless JSON value converters use.</summary>
    public JsonValueComparer()
        : this(EfCoreJsonOptions.Instance) { }

    private static bool _AreEqual(T? left, T? right, JsonSerializerOptions options)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return ReferenceEquals(left, right)
            || string.Equals(
                JsonSerializer.Serialize(left, options),
                JsonSerializer.Serialize(right, options),
                StringComparison.Ordinal
            );
    }

    private static int _GetHashCode(T? value, JsonSerializerOptions options)
    {
        return value is null ? 0 : StringComparer.Ordinal.GetHashCode(JsonSerializer.Serialize(value, options));
    }

    private static T _Snapshot(T? value, JsonSerializerOptions options)
    {
        return value is null
            ? value!
            : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;
    }
}
