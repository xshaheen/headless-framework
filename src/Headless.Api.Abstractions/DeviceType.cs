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
