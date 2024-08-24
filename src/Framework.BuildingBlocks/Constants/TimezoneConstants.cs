using TimeZoneConverter;

namespace Framework.BuildingBlocks.Constants;

#pragma warning disable CA1508
public static class TimezoneConstants
{
    private static readonly object _Lock = new();

    public const string GazaTime = "Asia/Gaza";
    public const string SaudiArabiaTime = "Asia/Riyadh";
    public const string EgyptStandardTime = "Africa/Cairo";

    private static TimeZoneInfo? _palestineTimeZone;

    public static TimeZoneInfo PalestineTimeZone
    {
        get
        {
            if (_palestineTimeZone is null)
            {
                lock (_Lock)
                {
                    _palestineTimeZone ??= TZConvert.GetTimeZoneInfo(GazaTime);
                }
            }

            return _palestineTimeZone;
        }
    }

    private static TimeZoneInfo? _saudiArabiaTimeZone;

    public static TimeZoneInfo SaudiArabiaTimeZone
    {
        get
        {
            if (_saudiArabiaTimeZone is null)
            {
                lock (_Lock)
                {
                    _saudiArabiaTimeZone ??= TZConvert.GetTimeZoneInfo(SaudiArabiaTime);
                }
            }

            return _saudiArabiaTimeZone;
        }
    }

    private static TimeZoneInfo? _egyptTimeZone;

    public static TimeZoneInfo EgyptTimeZone
    {
        get
        {
            if (_egyptTimeZone is null)
            {
                lock (_Lock)
                {
                    _egyptTimeZone ??= TZConvert.GetTimeZoneInfo(EgyptStandardTime);
                }
            }

            return _egyptTimeZone;
        }
    }
}
