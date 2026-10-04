// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Provides the time zone for the ambient execution scope.
/// </summary>
public interface ICurrentTimeZone
{
    /// <summary>Gets the current time zone.</summary>
    TimeZoneInfo TimeZone { get; }
}

/// <summary>
/// Implements <see cref="ICurrentTimeZone"/> returning the host machine local time zone.
/// </summary>
public sealed class LocalCurrentTimeZone : ICurrentTimeZone
{
    /// <inheritdoc/>
    public TimeZoneInfo TimeZone => TimeZoneInfo.Local;
}
