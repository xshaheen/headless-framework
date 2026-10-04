// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.Generator.Primitives;

/// <summary>
/// Provides conversion methods for <see cref="DateOnly"/> and <see cref="TimeOnly"/> values.
/// </summary>
/// <remarks>
/// This type is generator-output plumbing; it must stay public for emitted code but is not intended
/// for direct use.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DateOnlyExtensions
{
    /// <summary>
    /// Converts a <see cref="DateOnly"/> value to a <see cref="DateTime"/> with the time component set to the
    /// minimum value and the kind set to local.
    /// </summary>
    /// <param name="value">The date to convert.</param>
    /// <returns>A <see cref="DateTime"/> representation of the given <see cref="DateOnly"/> value.</returns>
    public static DateTime ToDateTime(this DateOnly value)
    {
        return value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
    }

    /// <summary>
    /// Converts a <see cref="TimeOnly"/> value to a <see cref="DateTime"/> with the date component set to the
    /// minimum value and the kind set to local.
    /// </summary>
    /// <param name="value">The time to convert.</param>
    /// <returns>A <see cref="DateTime"/> representation of the given <see cref="TimeOnly"/> value.</returns>
    public static DateTime ToDateTime(this TimeOnly value)
    {
        return new(value.Ticks, DateTimeKind.Local);
    }
}
