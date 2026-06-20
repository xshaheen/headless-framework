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
        }

        return null;
    }
}
