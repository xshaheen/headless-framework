// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Provides cross-platform time zone enumeration and identifier conversion between Windows and IANA formats.
/// Implementations should cache the enumeration lists because resolving a <see cref="TimeZoneInfo"/> per entry
/// is expensive.
/// </summary>
public interface ITimezoneProvider
{
    /// <summary>
    /// Retrieves a list of all known Windows time zones, ordered alphabetically.
    /// Each time zone includes its display name as name (offset from UTC) and value as the identifier name.
    /// </summary>
    /// <returns>A list of <see cref="TimezoneOption"/> objects representing Windows time zones.</returns>
    IReadOnlyList<TimezoneOption> GetWindowsTimezones();

    /// <summary>
    /// Retrieves a list of all known IANA time zones, ordered alphabetically.
    /// Each time zone includes its display name as name (offset from UTC) and value as the identifier name.
    /// </summary>
    /// <returns>A list of <see cref="TimezoneOption"/> objects representing IANA time zones.</returns>
    IReadOnlyList<TimezoneOption> GetIanaTimezones();

    /// <summary>
    /// Converts a Windows time zone ID to an equivalent IANA time zone name.
    /// </summary>
    /// <param name="windowsTimeZoneId">The Windows time zone ID to convert.</param>
    /// <returns>An IANA time zone name.</returns>
    /// <exception cref="InvalidTimeZoneException">
    /// Thrown if the input string was not recognized or has no equivalent IANA
    /// zone.
    /// </exception>
    string WindowsToIana(string windowsTimeZoneId);

    /// <summary>
    /// Converts an IANA time zone name to the equivalent Windows time zone ID.
    /// </summary>
    /// <param name="ianaTimeZoneName">The IANA time zone name to convert.</param>
    /// <returns>A Windows time zone ID.</returns>
    /// <exception cref="InvalidTimeZoneException">
    /// Thrown if the input string was not recognized or has no equivalent Windows
    /// zone.
    /// </exception>
    string IanaToWindows(string ianaTimeZoneName);

    /// <summary>
    /// Retrieves a <see cref="TimeZoneInfo" /> object given a valid Windows or IANA time zone identifier,
    /// regardless of which platform the application is running on.
    /// </summary>
    /// <param name="windowsOrIanaTimeZoneId">A valid Windows or IANA time zone identifier.</param>
    /// <returns>A <see cref="TimeZoneInfo" /> object.</returns>
    TimeZoneInfo GetTimeZoneInfo(string windowsOrIanaTimeZoneId);
}

/// <summary>
/// An immutable time-zone display option: a display <see cref="Name"/> (identifier plus UTC offset)
/// and the <see cref="Value"/> identifier. Being immutable, instances are safe to cache and share.
/// </summary>
[PublicAPI]
public sealed record TimezoneOption(string Name, string Value);
