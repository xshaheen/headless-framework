// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using TimeZoneConverter;

namespace Framework.Abstractions;

public interface ITimezoneProvider
{
    /// <summary>
    /// Retrieves a list of all known Windows time zones, ordered alphabetically.
    /// Each time zone includes its display name as name (offset from UTC) and value as the identifier name.
    /// </summary>
    /// <returns>A list of <see cref="NameValue"/> objects representing Windows time zones.</returns>
    List<NameValue> GetWindowsTimezones();

    /// <summary>
    /// Retrieves a list of all known IANA time zones, ordered alphabetically.
    /// Each time zone includes its display name as name (offset from UTC) and value as the identifier name.
    /// </summary>
    /// <returns>A list of <see cref="NameValue"/> objects representing IANA time zones.</returns>
    List<NameValue> GetIanaTimezones();

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
    public List<NameValue> GetWindowsTimezones()
    {
        return TZConvert
            .KnownWindowsTimeZoneIds.Order(StringComparer.Ordinal)
            .Select(value => new NameValue
            {
                Name = $"{value} ({_GetTimezoneOffset(TZConvert.GetTimeZoneInfo(value))})",
                Value = value,
            })
            .ToList();
    }

    public List<NameValue> GetIanaTimezones()
    {
        return TZConvert
            .KnownIanaTimeZoneNames.Order(StringComparer.Ordinal)
            .Select(value => new NameValue
            {
                Name = $"{value} ({_GetTimezoneOffset(TZConvert.GetTimeZoneInfo(value))})",
                Value = value,
            })
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
