// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

internal static class CronTimeZoneResolver
{
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

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            if (timeZone.HasIanaId)
            {
                return timeZone;
            }
        }
        catch (TimeZoneNotFoundException)
        {
            // Report one stable validation error below.
        }
        catch (InvalidTimeZoneException)
        {
            // Report one stable validation error below.
        }

        throw new ArgumentException($"Time zone '{timeZoneId}' must be a valid IANA identifier.", nameof(timeZoneId));
    }
}
