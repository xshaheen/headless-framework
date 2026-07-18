// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Jobs;

internal static class CronTimeZoneResolver
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> _IanaTimeZones = new(StringComparer.Ordinal);

    public static TimeZoneInfo Resolve(string? timeZoneId, TimeZoneInfo fallback)
    {
        if (timeZoneId is null)
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException(
                $"Time zone '{timeZoneId}' must be a valid IANA identifier.",
                nameof(timeZoneId)
            );
        }

        if (_IanaTimeZones.TryGetValue(timeZoneId, out var cached))
        {
            return cached;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) && timeZone.HasIanaId)
        {
            return _IanaTimeZones.GetOrAdd(timeZoneId, timeZone);
        }

        throw new ArgumentException($"Time zone '{timeZoneId}' must be a valid IANA identifier.", nameof(timeZoneId));
    }
}
