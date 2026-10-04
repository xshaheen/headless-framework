// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless;

/// <summary>
/// Commonly used time zones, each exposed as both an IANA time-zone ID string and a resolved
/// <see cref="TimeZoneInfo"/>. The <see cref="TimeZoneInfo"/> instances are resolved once through
/// <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>: Linux and macOS read IANA IDs from the system time-zone
/// database, and Windows resolves them through ICU, so on Windows they require ICU and fail in NLS or
/// globalization-invariant mode.
/// </summary>
[PublicAPI]
public static class TimezoneConstants
{
    /// <summary>IANA time-zone ID for Gaza (<c>Asia/Gaza</c>).</summary>
    public const string GazaTime = "Asia/Gaza";

    /// <summary><see cref="TimeZoneInfo"/> for <see cref="GazaTime"/>.</summary>
    public static TimeZoneInfo GazaTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(GazaTime);

    /// <summary>IANA time-zone ID for Saudi Arabia (<c>Asia/Riyadh</c>).</summary>
    public const string SaudiArabiaTime = "Asia/Riyadh";

    /// <summary><see cref="TimeZoneInfo"/> for <see cref="SaudiArabiaTime"/>.</summary>
    public static TimeZoneInfo SaudiArabiaTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(SaudiArabiaTime);

    /// <summary>IANA time-zone ID for Egypt (<c>Africa/Cairo</c>).</summary>
    public const string EgyptStandardTime = "Africa/Cairo";

    /// <summary><see cref="TimeZoneInfo"/> for <see cref="EgyptStandardTime"/>.</summary>
    public static TimeZoneInfo EgyptTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(EgyptStandardTime);
}
