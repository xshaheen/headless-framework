// Copyright (c) Mahmoud Shaheen. All rights reserved.

using TimeZoneConverter;

namespace Headless.Abstractions;

/// <summary>
/// An immutable time-zone display option: a display <see cref="Name"/> (identifier plus UTC offset)
/// and the <see cref="Value"/> identifier. Being immutable, instances are safe to cache and share.
/// </summary>
[PublicAPI]
public sealed record TimezoneOption(string Name, string Value);

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
/// <see cref="ITimezoneProvider"/> implementation backed by the <c>TimeZoneConverter</c> (TZConvert) library,
/// which maps between Windows and IANA time zone identifiers on any platform. The Windows and IANA option lists
/// are built once per process via <see cref="Lazy{T}"/> and shared across all calls; <see cref="TimezoneOption"/>
/// is immutable so the cached lists need no per-call copying.
/// </summary>
public sealed class TzConvertTimezoneProvider : ITimezoneProvider
{
    // The Windows/IANA zone tables are static for the process lifetime, so the option lists are built
    // once (resolving a TimeZoneInfo per entry is the expensive part) and returned directly on every
    // call. TimezoneOption is an immutable record and the list is read-only, so sharing the cache is
    // safe and needs no per-call recomputation or copying.
    private static readonly Lazy<IReadOnlyList<TimezoneOption>> _WindowsTimezones = new(() =>
        _BuildTimezones(TZConvert.KnownWindowsTimeZoneIds)
    );

    private static readonly Lazy<IReadOnlyList<TimezoneOption>> _IanaTimezones = new(() =>
        _BuildTimezones(TZConvert.KnownIanaTimeZoneNames)
    );

    /// <inheritdoc/>
    public IReadOnlyList<TimezoneOption> GetWindowsTimezones() => _WindowsTimezones.Value;

    /// <inheritdoc/>
    public IReadOnlyList<TimezoneOption> GetIanaTimezones() => _IanaTimezones.Value;

    private static IReadOnlyList<TimezoneOption> _BuildTimezones(IEnumerable<string> timeZoneIds)
    {
        return timeZoneIds
            .Order(StringComparer.Ordinal)
            .Select(value => new TimezoneOption(
                $"{value} ({_GetTimezoneOffset(TZConvert.GetTimeZoneInfo(value))})",
                value
            ))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public string WindowsToIana(string windowsTimeZoneId)
    {
        return TZConvert.WindowsToIana(windowsTimeZoneId);
    }

    /// <inheritdoc/>
    public string IanaToWindows(string ianaTimeZoneName)
    {
        return TZConvert.IanaToWindows(ianaTimeZoneName);
    }

    /// <inheritdoc/>
    public TimeZoneInfo GetTimeZoneInfo(string windowsOrIanaTimeZoneId)
    {
        return TZConvert.GetTimeZoneInfo(windowsOrIanaTimeZoneId);
    }

    private static string _GetTimezoneOffset(TimeZoneInfo timeZoneInfo)
    {
        return timeZoneInfo.BaseUtcOffset < TimeSpan.Zero
            ? "-" + timeZoneInfo.BaseUtcOffset.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : "+" + timeZoneInfo.BaseUtcOffset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }
}
