// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Parses human-friendly duration strings into <see cref="TimeSpan"/> values. The numeric magnitude is followed by a
/// unit suffix: <c>nanos</c> (nanoseconds), <c>micros</c> (microseconds), <c>ms</c> (milliseconds), <c>s</c>
/// (seconds), <c>m</c> (minutes), <c>h</c> (hours), or <c>d</c> (days). For example, <c>"30s"</c> or <c>"2h"</c>.
/// </summary>
[PublicAPI]
public static class TimeUnit
{
    /// <summary>Parses <paramref name="value"/> into a <see cref="TimeSpan"/>.</summary>
    /// <param name="value">The duration string to parse (for example <c>"30s"</c> or <c>"2h"</c>).</param>
    /// <returns>The parsed <see cref="TimeSpan"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty or cannot be parsed as a valid time value.</exception>
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

    /// <summary>Attempts to parse <paramref name="value"/> into a <see cref="TimeSpan"/> without throwing.</summary>
    /// <param name="value">The duration string to parse (for example <c>"30s"</c> or <c>"2h"</c>).</param>
    /// <param name="time">When this method returns, contains the parsed <see cref="TimeSpan"/>, or <see langword="null"/> when parsing fails.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> was parsed successfully; otherwise, <see langword="false"/>.</returns>
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
        // Trim a span instead of allocating a lowercased string. Suffix checks are case-insensitive (the original
        // lowercased before matching); the numeric portion is sliced off before parsing, so case never affects parsing.
        var trimmed = value.AsSpan().Trim();

        try
        {
            // The minutes branch stays case-sensitive (uppercase 'M' could mean months) but tests the TRIMMED
            // span, so a trailing space (e.g. "5m ") still matches.
            if (trimmed.EndsWith('m'))
            {
                return int.TryParse(trimmed[..^1], CultureInfo.InvariantCulture, out var minutes)
                    ? new TimeSpan(0, minutes, 0)
                    : null;
            }

            if (trimmed.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(trimmed[..^1], CultureInfo.InvariantCulture, out var hours)
                    ? new TimeSpan(hours, 0, 0)
                    : null;
            }

            if (trimmed.EndsWith("d", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(trimmed[..^1], CultureInfo.InvariantCulture, out var days)
                    ? new TimeSpan(days, 0, 0, 0)
                    : null;
            }

            if (trimmed.EndsWith("nanos", StringComparison.OrdinalIgnoreCase))
            {
                // Cast to long: 100-ns ticks above int.MaxValue (~214s) must not truncate.
                return long.TryParse(trimmed[..^5], CultureInfo.InvariantCulture, out var nanoseconds)
                    ? new TimeSpan((long)Math.Round(nanoseconds / 100d, MidpointRounding.AwayFromZero))
                    : null;
            }

            if (trimmed.EndsWith("micros", StringComparison.OrdinalIgnoreCase))
            {
                return long.TryParse(trimmed[..^6], CultureInfo.InvariantCulture, out var microseconds)
                    ? new TimeSpan(microseconds * 10)
                    : null;
            }

            if (trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(trimmed[..^2], CultureInfo.InvariantCulture, out var milliseconds)
                    ? new TimeSpan(0, 0, 0, 0, milliseconds)
                    : null;
            }

            if (trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(trimmed[..^1], CultureInfo.InvariantCulture, out var seconds)
                    ? new TimeSpan(0, 0, seconds)
                    : null;
            }

            return null;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException)
        {
            // A magnitude that parses as int/long but exceeds TimeSpan's range surfaces as a failed parse
            // (TryParse -> false, Parse -> ArgumentException) instead of an uncaught throw.
            return null;
        }
    }
}
