// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Provides the time zone that should be treated as "current" for the ambient scope (request, job, or user session).
/// Implementations may derive the zone from the HTTP request, the authenticated user's profile, application
/// configuration, or the host's local time zone.
/// </summary>
public interface ICurrentTimeZone
{
    /// <summary>Gets the current time zone.</summary>
    TimeZoneInfo TimeZone { get; }
}
