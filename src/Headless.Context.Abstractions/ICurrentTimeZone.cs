// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Provides the time zone that should be treated as current for the ambient scope (request, job, or user session).
/// </summary>
/// <remarks>
/// Implementations may derive the zone from the HTTP request, the authenticated user's profile,
/// application configuration, or the host's local time zone.
/// </remarks>
public interface ICurrentTimeZone
{
    /// <summary>Gets the current time zone.</summary>
    TimeZoneInfo TimeZone { get; }
}

/// <summary>
/// <see cref="ICurrentTimeZone"/> implementation that always returns the host machine's local time zone
/// (<see cref="TimeZoneInfo.Local"/>).
/// </summary>
/// <remarks>
/// Suitable for single-region deployments where the server and users share the same time zone.
/// </remarks>
public sealed class LocalCurrentTimeZone : ICurrentTimeZone
{
    /// <inheritdoc/>
    public TimeZoneInfo TimeZone => TimeZoneInfo.Local;
}
