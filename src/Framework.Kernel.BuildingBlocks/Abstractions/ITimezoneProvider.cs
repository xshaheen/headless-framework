using Framework.Kernel.Primitives;
using TimeZoneConverter;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface ITimezoneProvider
{
    List<NameValue> GetWindowsTimezones();

    List<NameValue> GetIanaTimezones();

    string WindowsToIana(string windowsTimeZoneId);

    string IanaToWindows(string ianaTimeZoneName);

    TimeZoneInfo GetTimeZoneInfo(string windowsOrIanaTimeZoneId);
}

public sealed class TzConvertTimezoneProvider : ITimezoneProvider
{
    public List<NameValue> GetWindowsTimezones()
    {
        return TZConvert
            .KnownWindowsTimeZoneIds.Order(StringComparer.Ordinal)
            .Select(x => new NameValue { Name = x, Value = x })
            .ToList();
    }

    public List<NameValue> GetIanaTimezones()
    {
        return TZConvert
            .KnownIanaTimeZoneNames.Order(StringComparer.Ordinal)
            .Select(x => new NameValue { Name = x, Value = x })
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
}
