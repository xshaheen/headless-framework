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

    private static TimeSpan? _ParseTime(string value)
    {
        // compare using the original value as uppercase M could mean months.
        var normalized = value.ToLowerInvariant().Trim();

        if (value.EndsWith('m'))
        {
            if (
                int.TryParse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture, out var minutes)
            )
            {
                return new TimeSpan(0, minutes, 0);
            }

            return null;
        }

        if (normalized.EndsWith('h'))
        {
            if (int.TryParse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture, out var hours))
            {
                return new TimeSpan(hours, 0, 0);
            }

            return null;
        }

        if (normalized.EndsWith('d'))
        {
            if (int.TryParse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture, out var days))
            {
                return new TimeSpan(days, 0, 0, 0);
            }

            return null;
        }

        if (normalized.EndsWith("nanos", StringComparison.Ordinal))
        {
            if (
                long.TryParse(
                    normalized.AsSpan(0, normalized.Length - 5),
                    CultureInfo.InvariantCulture,
                    out var nanoseconds
                )
            )
            {
                return new TimeSpan((int)Math.Round(nanoseconds / 100d));
            }

            return null;
        }

        if (normalized.EndsWith("micros", StringComparison.Ordinal))
        {
            if (
                long.TryParse(
                    normalized.AsSpan(0, normalized.Length - 6),
                    CultureInfo.InvariantCulture,
                    out var microseconds
                )
            )
            {
                return new TimeSpan(microseconds * 10);
            }

            return null;
        }

        if (normalized.EndsWith("ms", StringComparison.Ordinal))
        {
            if (
                int.TryParse(
                    normalized.AsSpan(0, normalized.Length - 2),
                    CultureInfo.InvariantCulture,
                    out var milliseconds
                )
            )
            {
                return new TimeSpan(0, 0, 0, 0, milliseconds);
            }

            return null;
        }

        if (normalized.EndsWith('s'))
        {
            if (
                int.TryParse(normalized.AsSpan(0, normalized.Length - 1), CultureInfo.InvariantCulture, out var seconds)
            )
            {
                return new TimeSpan(0, 0, seconds);
            }

            return null;
        }

        return null;
    }
}
