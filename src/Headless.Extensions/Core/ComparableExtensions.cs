// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Range-test extensions for any <see cref="IComparable{T}"/> value.</summary>
[PublicAPI]
public static class ComparableExtensions
{
    /// <summary>
    /// Checks whether the value lies strictly between the two specified bounds (both bounds excluded).
    /// </summary>
    /// <typeparam name="T">The comparable type of the value and bounds.</typeparam>
    /// <param name="value">The value to be checked</param>
    /// <param name="minValue">Minimum (exclusive) value</param>
    /// <param name="maxValue">Maximum (exclusive) value</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="value"/> is greater than <paramref name="minValue"/> and less than
    /// <paramref name="maxValue"/>; otherwise <see langword="false"/>.
    /// </returns>
    public static bool ExclusiveBetween<T>(this T value, T minValue, T maxValue)
        where T : IComparable<T>
    {
        return value.CompareTo(minValue) > 0 && value.CompareTo(maxValue) < 0;
    }

    /// <summary>
    /// Checks whether the value lies within the two specified bounds (both bounds included).
    /// </summary>
    /// <typeparam name="T">The comparable type of the value and bounds.</typeparam>
    /// <param name="value">The value to be checked</param>
    /// <param name="minValue">Minimum (inclusive) value</param>
    /// <param name="maxValue">Maximum (inclusive) value</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="value"/> is greater than or equal to <paramref name="minValue"/> and
    /// less than or equal to <paramref name="maxValue"/>; otherwise <see langword="false"/>.
    /// </returns>
    public static bool InclusiveBetween<T>(this T value, T minValue, T maxValue)
        where T : IComparable<T>
    {
        return value.CompareTo(minValue) >= 0 && value.CompareTo(maxValue) <= 0;
    }
}
