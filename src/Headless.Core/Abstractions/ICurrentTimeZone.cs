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

/// <summary>
/// <see cref="ICurrentTimeZone"/> implementation that always returns the host machine's local time zone
/// (<see cref="TimeZoneInfo.Local"/>). Suitable for single-region deployments where the server and users
/// share the same time zone.
/// </summary>
public sealed class LocalCurrentTimeZone : ICurrentTimeZone
{
    /// <inheritdoc/>
    public TimeZoneInfo TimeZone => TimeZoneInfo.Local;
}
