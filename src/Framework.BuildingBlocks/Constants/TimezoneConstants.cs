// Copyright (c) Mahmoud Shaheen. All rights reserved.

using TimeZoneConverter;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

[PublicAPI]
public static class TimezoneConstants
{
    public const string GazaTime = "Asia/Gaza";

    public static TimeZoneInfo GazaTimeZone { get; } = TZConvert.GetTimeZoneInfo(GazaTime);

    public const string SaudiArabiaTime = "Asia/Riyadh";

    public static TimeZoneInfo SaudiArabiaTimeZone { get; } = TZConvert.GetTimeZoneInfo(SaudiArabiaTime);

    public const string EgyptStandardTime = "Africa/Cairo";

    public static TimeZoneInfo EgyptTimeZone { get; } = TZConvert.GetTimeZoneInfo(EgyptStandardTime);
}
