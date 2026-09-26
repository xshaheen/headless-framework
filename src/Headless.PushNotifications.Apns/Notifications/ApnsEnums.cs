// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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

/// <summary>
/// The Live Activity lifecycle event a Live Activity push carries, sent as the <c>"event"</c> key.
/// </summary>
[PublicAPI]
public enum ApnsLiveActivityEvent
{
    /// <summary>Starts a new Live Activity. Send it to the app's push-to-start token.</summary>
    Start = 0,

    /// <summary>Updates a running Live Activity. Send it to the activity's push token.</summary>
    Update = 1,

    /// <summary>Ends a running Live Activity. Send it to the activity's push token.</summary>
    End = 2,
}
