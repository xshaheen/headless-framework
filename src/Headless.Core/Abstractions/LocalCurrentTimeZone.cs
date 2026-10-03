// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

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
