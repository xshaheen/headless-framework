// Copyright (c) Mahmoud Shaheen. All rights reserved.

using TimeZoneConverter;

namespace Headless.Constants;

/// <summary>
/// Commonly used time zones, each exposed as both an IANA time-zone ID string and a resolved
/// <see cref="TimeZoneInfo"/>. The <see cref="TimeZoneInfo"/> instances are resolved once via
/// <c>TZConvert</c> so the IANA IDs work cross-platform (including Windows, which natively uses
/// Windows time-zone IDs).
/// </summary>
[PublicAPI]
public static class TimezoneConstants
{
    /// <summary>IANA time-zone ID for Gaza (<c>Asia/Gaza</c>).</summary>
    public const string GazaTime = "Asia/Gaza";

    /// <summary><see cref="TimeZoneInfo"/> for <see cref="GazaTime"/>.</summary>
    public static TimeZoneInfo GazaTimeZone { get; } = TZConvert.GetTimeZoneInfo(GazaTime);

    /// <summary>IANA time-zone ID for Saudi Arabia (<c>Asia/Riyadh</c>).</summary>
    public const string SaudiArabiaTime = "Asia/Riyadh";

    /// <summary><see cref="TimeZoneInfo"/> for <see cref="SaudiArabiaTime"/>.</summary>
    public static TimeZoneInfo SaudiArabiaTimeZone { get; } = TZConvert.GetTimeZoneInfo(SaudiArabiaTime);

    /// <summary>IANA time-zone ID for Egypt (<c>Africa/Cairo</c>).</summary>
    public const string EgyptStandardTime = "Africa/Cairo";

    /// <summary><see cref="TimeZoneInfo"/> for <see cref="EgyptStandardTime"/>.</summary>
    public static TimeZoneInfo EgyptTimeZone { get; } = TZConvert.GetTimeZoneInfo(EgyptStandardTime);
}
