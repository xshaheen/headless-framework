// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Jobs;

internal static class CronTimeZoneResolver
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> _IanaTimeZones = new(StringComparer.Ordinal);

    public static TimeZoneInfo Resolve(string? timeZoneId, TimeZoneInfo fallback)
    {
        return TryResolve(timeZoneId, fallback, out var timeZone)
            ? timeZone
            : throw new ArgumentException(
                $"Time zone '{timeZoneId}' must be a valid IANA identifier.",
                nameof(timeZoneId)
            );
    }

    /// <summary>
    /// Resolves <paramref name="timeZoneId"/> without throwing, so a caller can classify the failure instead of
    /// catching it.
    /// </summary>
    /// <remarks>
    /// Whether a zone resolves is a property of THIS HOST's timezone database, not of the definition naming it (#830).
    /// A caller that must decide between a node-local condition and a fleet-wide definitional error therefore probes
    /// here rather than treating the <see cref="ArgumentException"/> from <see cref="Resolve"/> as evidence about the
    /// definition — the same identifier resolves fine on a peer with current tzdata.
    /// </remarks>
    public static bool TryResolve(
        string? timeZoneId,
        TimeZoneInfo fallback,
        [NotNullWhen(true)] out TimeZoneInfo? timeZone
    )
    {
        if (timeZoneId is null)
        {
            timeZone = fallback;

            return true;
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = null;

            return false;
        }

        if (_IanaTimeZones.TryGetValue(timeZoneId, out var cached))
        {
            timeZone = cached;

            return true;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) && found.HasIanaId)
        {
            timeZone = _IanaTimeZones.GetOrAdd(timeZoneId, found);

            return true;
        }

        timeZone = null;

        return false;
    }
}
