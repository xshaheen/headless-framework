// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// How urgently the system presents an alert notification, sent as the <c>interruption-level</c> key.
/// </summary>
[PublicAPI]
public enum ApnsInterruptionLevel
{
    /// <summary>Added to the notification list without lighting the screen or playing a sound.</summary>
    Passive = 0,

    /// <summary>Presented immediately, lighting the screen and able to play a sound. Apple's default.</summary>
    Active = 1,

    /// <summary>Presented immediately and able to break through a Focus that allows time-sensitive notifications.</summary>
    TimeSensitive = 2,

    /// <summary>
    /// Presented immediately, bypassing the mute switch and Focus. Requires Apple's critical-alerts entitlement.
    /// </summary>
    Critical = 3,
}
