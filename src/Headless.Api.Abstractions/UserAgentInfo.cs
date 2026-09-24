// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Broad class of device a <c>User-Agent</c> identifies.</summary>
/// <remarks>
/// Deliberately coarser than the detector's own taxonomy: phones and feature phones both report
/// <see cref="Phone"/>, phablets report <see cref="Tablet"/>. Callers that persist this value should store the
/// name rather than the number, so the set can gain members without rewriting stored rows.
/// </remarks>
[PublicAPI]
public enum DeviceType
{
    /// <summary>The device could not be identified.</summary>
    Unknown = 0,

    /// <summary>A desktop or laptop computer.</summary>
    Desktop = 1,

    /// <summary>A smartphone or feature phone.</summary>
    Phone = 2,

    /// <summary>A tablet or phablet.</summary>
    Tablet = 3,

    /// <summary>A games console.</summary>
    Console = 4,

    /// <summary>A television or set-top box.</summary>
    Tv = 5,

    /// <summary>An in-car browser.</summary>
    CarBrowser = 6,

    /// <summary>A smart display.</summary>
    SmartDisplay = 7,

    /// <summary>A smart speaker.</summary>
    SmartSpeaker = 8,

    /// <summary>A wearable, such as a watch.</summary>
    Wearable = 9,

    /// <summary>A camera.</summary>
    Camera = 10,

    /// <summary>A portable media player.</summary>
    PortableMediaPlayer = 11,

    /// <summary>A bot, crawler, or other automated client.</summary>
    Bot = 12,
}

/// <summary>Everything <see cref="IUserAgentParser"/> could identify from one <c>User-Agent</c> header value.</summary>
/// <remarks>
/// <para>
/// Every member except <see cref="UserAgent"/>, <see cref="IsBot"/> and <see cref="Device"/> is
/// <see langword="null"/> when the detector could not identify it. A User-Agent is a self-reported,
/// trivially-forged string: treat this as a hint for diagnostics, session display, and analytics, never as an
/// authorization or security input.
/// </para>
/// <para>
/// Bot fields are populated only when <see cref="IsBot"/> is <see langword="true"/>, and a bot rarely reports an
/// operating system or a client version — expect those to be <see langword="null"/> for one;
/// <see cref="Device"/> is always <see cref="DeviceType.Bot"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record UserAgentInfo
{
    /// <summary>The header value this was parsed from, after any length capping the parser applied.</summary>
    public required string UserAgent { get; init; }

    /// <summary>Whether the client identified itself as a bot or crawler.</summary>
    public required bool IsBot { get; init; }

    /// <summary>The broad class of device, or <see cref="DeviceType.Unknown"/> when it could not be identified.</summary>
    public required DeviceType Device { get; init; }

    /// <summary>Device brand, for example <c>Apple</c> or <c>Samsung</c>.</summary>
    public string? DeviceBrand { get; init; }

    /// <summary>Device model, for example <c>iPhone 13</c>.</summary>
    public string? DeviceModel { get; init; }

    /// <summary>Operating system name, for example <c>Windows</c> or <c>Android</c>.</summary>
    public string? OsName { get; init; }

    /// <summary>Operating system version, for example <c>14.5</c>.</summary>
    public string? OsVersion { get; init; }

    /// <summary>Operating system platform, for example <c>x64</c> or <c>ARM</c>.</summary>
    public string? OsPlatform { get; init; }

    /// <summary>Client name, for example <c>Chrome</c> or <c>Facebook App</c>.</summary>
    public string? ClientName { get; init; }

    /// <summary>Client version.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>Client category reported by the detector, for example <c>browser</c> or <c>feed reader</c>.</summary>
    public string? ClientType { get; init; }

    /// <summary>Rendering engine, for example <c>Blink</c> or <c>Gecko</c>. Only browsers report one.</summary>
    public string? ClientEngine { get; init; }

    /// <summary>Bot name when <see cref="IsBot"/> is <see langword="true"/>, for example <c>Googlebot</c>.</summary>
    public string? BotName { get; init; }

    /// <summary>Bot category when <see cref="IsBot"/> is <see langword="true"/>, for example <c>Search bot</c>.</summary>
    public string? BotCategory { get; init; }

    /// <summary>
    /// Operating system and client names joined for display, for example <c>"Windows Chrome"</c>, or
    /// <see langword="null"/> when neither was identified.
    /// </summary>
    /// <remarks>This is what <see cref="IUserAgentParser.GetDeviceInfo"/> returns.</remarks>
    public string? Summary =>
        (OsName.IsNullOrWhiteSpace(), ClientName.IsNullOrWhiteSpace()) switch
        {
            (false, false) => OsName + " " + ClientName,
            (false, true) => OsName,
            (true, false) => ClientName,
            _ => null,
        };
}
