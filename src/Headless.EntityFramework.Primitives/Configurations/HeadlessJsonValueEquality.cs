// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

/// <summary>
/// Equality for loosely typed values stored inside a JSON column (a <c>File.Metadata</c> entry or an
/// <c>ExtraProperties</c> value): two values are equal when the column would store the same JSON for them.
/// </summary>
/// <remarks>
/// Values of the same simple CLR type compare by value without serializing, which is the common case on every
/// change-detection pass. A value read back from the column often has a different CLR type than the one the
/// application assigned (a stored <see cref="int"/> reads back as <see cref="long"/>, an object as
/// <see cref="JsonElement"/>), so differing types, and anything that is not a simple value, fall back to comparing
/// the serialized JSON, as the JSON comparer does.
/// </remarks>
internal static class HeadlessJsonValueEquality
{
    public static bool AreEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is JsonElement leftElement && right is JsonElement rightElement)
        {
            return JsonElement.DeepEquals(leftElement, rightElement);
        }

        if (left.GetType() == right.GetType() && _IsSimple(left))
        {
            return left.Equals(right);
        }

        return string.Equals(_Serialize(left), _Serialize(right), StringComparison.Ordinal);
    }

    public static bool AreEqual(Dictionary<string, object?>? left, Dictionary<string, object?>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !AreEqual(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Order-independent hash of the keys only: values that are equal across CLR types hash differently, so hashing
    /// them would break the equality contract.
    /// </summary>
    public static int GetHashCode(Dictionary<string, object?>? dictionary)
    {
        if (dictionary is null)
        {
            return 0;
        }

        var hash = dictionary.Count;

        foreach (var key in dictionary.Keys)
        {
            hash ^= StringComparer.Ordinal.GetHashCode(key);
        }

        return hash;
    }

    /// <summary>
    /// A copy whose later comparison detects an in-place edit of the original: simple values are immutable and kept,
    /// anything else is copied through a JSON round trip.
    /// </summary>
    public static object? Snapshot(object? value)
    {
        return value is null or JsonElement || _IsSimple(value)
            ? value
            : JsonSerializer.Deserialize<object>(_Serialize(value), EfCoreJsonOptions.Instance);
    }

    private static bool _IsSimple(object value)
    {
        return value
            is string
                or bool
                or char
                or byte
                or sbyte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal
                or DateTime
                or DateTimeOffset
                or DateOnly
                or TimeOnly
                or TimeSpan
                or Guid
                or Enum;
    }

    private static string _Serialize(object value) => JsonSerializer.Serialize(value, EfCoreJsonOptions.Instance);
}
