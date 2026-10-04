// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using Headless.Abstractions;
using TimeZoneConverter;

namespace Headless.Api;

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
    public IReadOnlyList<TimezoneOption> GetWindowsTimezones()
    {
        return _WindowsTimezones.Value;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TimezoneOption> GetIanaTimezones()
    {
        return _IanaTimezones.Value;
    }

    private static ReadOnlyCollection<TimezoneOption> _BuildTimezones(IEnumerable<string> timeZoneIds)
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
