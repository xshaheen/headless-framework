// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class DateOnlyExtensions
{
    /// <summary>
    /// Converts a <see cref="DateOnly"/> to a <see cref="DateTimeOffset"/> in the specified <see cref="TimeZoneInfo"/>.
    /// The time is set to midnight (00:00:00) and the correct offset (including DST if applicable) is applied.
    /// </summary>
    /// <param name="dateOnly">The <see cref="DateOnly"/> to convert.</param>
    /// <param name="timezone">The target <see cref="TimeZoneInfo"/>.</param>
    /// <returns>A <see cref="DateTimeOffset"/> representing the date at midnight in the specified timezone.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset AsTimezone(this DateOnly dateOnly, TimeZoneInfo timezone)
    {
        Argument.IsNotNull(timezone);
        var dateTime = new DateTime(dateOnly.Year, dateOnly.Month, dateOnly.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var offset = timezone.GetUtcOffset(dateTime); // Use GetUtcOffset to account for DST if applicable

        return new DateTimeOffset(dateTime, offset);
    }
}
