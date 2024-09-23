// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Provides a set of extension methods for operations on <see cref="IComparable{T}"/>.</summary>
[PublicAPI]
public static class ComparableExtensions
{
    /// <summary>
    /// Checks whether the value is in a range between the two specified
    /// numbers (exclusive).
    /// </summary>
    /// <param name="value">The value to be checked</param>
    /// <param name="minValue">Minimum (exclusive) value</param>
    /// <param name="maxValue">Maximum (exclusive) value</param>
    public static bool ExclusiveBetween<T>(this T value, T minValue, T maxValue)
        where T : IComparable<T>
    {
        return value.CompareTo(minValue) > 0 && value.CompareTo(maxValue) < 0;
    }

    /// <summary>
    /// Checks whether the value is in a range between the two specified
    /// numbers (inclusive).
    /// </summary>
    /// <param name="value">The value to be checked</param>
    /// <param name="minValue">Minimum (inclusive) value</param>
    /// <param name="maxValue">Maximum (inclusive) value</param>
    public static bool InclusiveBetween<T>(this T value, T minValue, T maxValue)
        where T : IComparable<T>
    {
        return value.CompareTo(minValue) >= 0 && value.CompareTo(maxValue) <= 0;
    }
}
