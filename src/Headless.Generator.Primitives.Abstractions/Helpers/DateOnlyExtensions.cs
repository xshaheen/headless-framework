// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.Generator.Primitives;

/// <summary>Provides conversion methods for <see cref="DateOnly"/> and <see cref="TimeOnly"/> values.</summary>
/// <remarks>Internal generator-emitted plumbing. Kept public for generated code execution.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DateOnlyExtensions
{
    /// <summary>Converts a <see cref="DateOnly"/> value to a <see cref="DateTime"/> at midnight with local kind.</summary>
    /// <param name="value">The date to convert.</param>
    /// <returns>A local <see cref="DateTime"/> instance.</returns>
    public static DateTime ToDateTime(this DateOnly value)
    {
        return value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
    }

    /// <summary>Converts a <see cref="TimeOnly"/> value to a <see cref="DateTime"/> with local kind.</summary>
    /// <param name="value">The time to convert.</param>
    /// <returns>A local <see cref="DateTime"/> instance.</returns>
    public static DateTime ToDateTime(this TimeOnly value)
    {
        return new(value.Ticks, DateTimeKind.Local);
    }
}
