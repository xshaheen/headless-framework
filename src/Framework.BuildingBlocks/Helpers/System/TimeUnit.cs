// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.BuildingBlocks.Helpers.System;

[PublicAPI]
public static class TimeUnit
{
    public static TimeSpan Parse(string value)
    {
        Argument.IsNotNullOrEmpty(value);

        var time = _ParseTime(value);

        if (time.HasValue)
        {
            return time.Value;
        }

        throw new ArgumentException($"Unable to parse value '{value}' as a valid time value", nameof(value));
    }

    public static bool TryParse(string value, out TimeSpan? time)
    {
        time = null;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        time = _ParseTime(value);
        return time.HasValue;
    }

    // TODO bug on int parse
    private static TimeSpan? _ParseTime(string value)
    {
        // compare using the original value as uppercase M could mean months.
        var normalized = value.ToLowerInvariant().Trim();
        if (value.EndsWith('m'))
        {
            var minutes = int.Parse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture);
            return new TimeSpan(0, minutes, 0);
        }

        if (normalized.EndsWith('h'))
        {
            var hours = int.Parse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture);
            return new TimeSpan(hours, 0, 0);
        }

        if (normalized.EndsWith('d'))
        {
            var days = int.Parse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture);
            return new TimeSpan(days, 0, 0, 0);
        }

        if (normalized.EndsWith("nanos", StringComparison.Ordinal))
        {
            var nanoseconds = long.Parse(normalized.AsSpan(0, normalized.Length - 5), CultureInfo.InvariantCulture);
            return new TimeSpan((int)Math.Round(nanoseconds / 100d));
        }

        if (normalized.EndsWith("micros", StringComparison.Ordinal))
        {
            var microseconds = long.Parse(normalized.AsSpan(0, normalized.Length - 6), CultureInfo.InvariantCulture);
            return new TimeSpan(microseconds * 10);
        }

        if (normalized.EndsWith("ms", StringComparison.Ordinal))
        {
            var milliseconds = int.Parse(normalized.AsSpan(0, normalized.Length - 2), CultureInfo.InvariantCulture);
            return new TimeSpan(0, 0, 0, 0, milliseconds);
        }

        if (normalized.EndsWith('s'))
        {
            var seconds = int.Parse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture);
            return new TimeSpan(0, 0, seconds);
        }

        return null;
    }
    // private static TimeSpan? _ParseTime(string value)
    // {
    //     // Normalize and trim the input
    //     var normalized = value.ToLowerInvariant().Trim();
    //
    //     if (normalized.EndsWith('m') && int.TryParse(normalized.AsSpan(0, normalized.Length - 1), out var minutes))
    //     {
    //         return new TimeSpan(0, minutes, 0);
    //     }
    //
    //     if (normalized.EndsWith('h') && int.TryParse(normalized.AsSpan(0, normalized.Length - 1), out var hours))
    //     {
    //         return new TimeSpan(hours, 0, 0);
    //     }
    //
    //     if (normalized.EndsWith('d') && int.TryParse(normalized.AsSpan(0, normalized.Length - 1), out var days))
    //     {
    //         return new TimeSpan(days, 0, 0, 0);
    //     }
    //
    //     if (normalized.EndsWith("nanos", StringComparison.Ordinal) &&
    //         long.TryParse(normalized.AsSpan(0, normalized.Length - 5), out var nanoseconds))
    //     {
    //         return new TimeSpan((int)Math.Round(nanoseconds / 100d));
    //     }
    //
    //     if (normalized.EndsWith("micros", StringComparison.Ordinal) &&
    //         long.TryParse(normalized.AsSpan(0, normalized.Length - 6), out var microseconds))
    //     {
    //         return new TimeSpan(microseconds * 10);
    //     }
    //
    //     if (normalized.EndsWith("ms", StringComparison.Ordinal) &&
    //         int.TryParse(normalized.AsSpan(0, normalized.Length - 2), out var milliseconds))
    //     {
    //         return new TimeSpan(0, 0, 0, 0, milliseconds);
    //     }
    //
    //     if (normalized.EndsWith('s') && int.TryParse(normalized.AsSpan(0, normalized.Length - 1), out var seconds))
    //     {
    //         return new TimeSpan(0, 0, seconds);
    //     }
    //
    //     // If no valid match, return null
    //     return null;
    // }
}
