// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using TimeZoneConverter;

namespace Headless.Abstractions;

public interface ITimezoneProvider
{
    /// <summary>
    /// Retrieves a list of all known Windows time zones, ordered alphabetically.
    /// Each time zone includes its display name as name (offset from UTC) and value as the identifier name.
    /// </summary>
    /// <returns>A list of <see cref="NameValue"/> objects representing Windows time zones.</returns>
    IReadOnlyList<NameValue> GetWindowsTimezones();

    /// <summary>
    /// Retrieves a list of all known IANA time zones, ordered alphabetically.
    /// Each time zone includes its display name as name (offset from UTC) and value as the identifier name.
    /// </summary>
    /// <returns>A list of <see cref="NameValue"/> objects representing IANA time zones.</returns>
    IReadOnlyList<NameValue> GetIanaTimezones();

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

public sealed class TzConvertTimezoneProvider : ITimezoneProvider
{
    // The Windows/IANA zone tables are static for the process lifetime, so the display strings
    // (resolving a TimeZoneInfo per entry is the expensive part) are computed once and cached as
    // immutable (name, id) pairs. A fresh NameValue list is projected per call so callers cannot
    // mutate a process-wide shared cache — NameValue has public setters.
    private static readonly Lazy<IReadOnlyList<(string Name, string Value)>> _WindowsTimezones = new(() =>
        _BuildTimezones(TZConvert.KnownWindowsTimeZoneIds)
    );

    private static readonly Lazy<IReadOnlyList<(string Name, string Value)>> _IanaTimezones = new(() =>
        _BuildTimezones(TZConvert.KnownIanaTimeZoneNames)
    );

    public IReadOnlyList<NameValue> GetWindowsTimezones() => _Project(_WindowsTimezones.Value);

    public IReadOnlyList<NameValue> GetIanaTimezones() => _Project(_IanaTimezones.Value);

    private static List<NameValue> _Project(IReadOnlyList<(string Name, string Value)> source)
    {
        var result = new List<NameValue>(source.Count);

        foreach (var (name, value) in source)
        {
            result.Add(new NameValue { Name = name, Value = value });
        }

        return result;
    }

    private static IReadOnlyList<(string Name, string Value)> _BuildTimezones(IEnumerable<string> timeZoneIds)
    {
        return timeZoneIds
            .Order(StringComparer.Ordinal)
            .Select(value => (Name: $"{value} ({_GetTimezoneOffset(TZConvert.GetTimeZoneInfo(value))})", Value: value))
            .ToList();
    }

    public string WindowsToIana(string windowsTimeZoneId)
    {
        return TZConvert.WindowsToIana(windowsTimeZoneId);
    }

    public string IanaToWindows(string ianaTimeZoneName)
    {
        return TZConvert.IanaToWindows(ianaTimeZoneName);
    }

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
