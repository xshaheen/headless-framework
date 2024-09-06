using TimeZoneConverter;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.BuildingBlocks;

[PublicAPI]
public static class TimezoneConstants
{
    public const string GazaTime = "Asia/Gaza";
    public const string SaudiArabiaTime = "Asia/Riyadh";
    public const string EgyptStandardTime = "Africa/Cairo";

    public static TimeZoneInfo PalestineTimeZone { get; } = TZConvert.GetTimeZoneInfo(GazaTime);

    public static TimeZoneInfo SaudiArabiaTimeZone { get; } = TZConvert.GetTimeZoneInfo(SaudiArabiaTime);

    public static TimeZoneInfo EgyptTimeZone { get; } = TZConvert.GetTimeZoneInfo(EgyptStandardTime);
}
