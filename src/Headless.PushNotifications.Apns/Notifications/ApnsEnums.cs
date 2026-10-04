// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>
/// Specifies the presentation urgency level of an APNs alert notification.
/// </summary>
[PublicAPI]
public enum ApnsInterruptionLevel
{
    /// <summary>Appends to the notification list without waking the screen or playing audio.</summary>
    Passive = 0,

    /// <summary>Displays immediately, wakes the screen, and plays audio.</summary>
    Active = 1,

    /// <summary>Displays immediately and bypasses Focus modes configured for time-sensitive notifications.</summary>
    TimeSensitive = 2,

    /// <summary>
    /// Displays immediately and bypasses the mute switch and Focus filters. Requires critical alert entitlement.
    /// </summary>
    Critical = 3,
}

/// <summary>
/// Specifies the lifecycle event carried by a Live Activity notification.
/// </summary>
[PublicAPI]
public enum ApnsLiveActivityEvent
{
    /// <summary>Initializes a new Live Activity using a push-to-start token.</summary>
    Start = 0,

    /// <summary>Updates an active Live Activity.</summary>
    Update = 1,

    /// <summary>Terminates an active Live Activity.</summary>
    End = 2,
}

/// <summary>
/// Specifies the notification push type for raw and typed APNs payloads.
/// </summary>
[PublicAPI]
public enum ApnsNotificationType
{
    /// <summary>
    /// User-visible notification dispatched to the main bundle identifier topic.
    /// </summary>
    Alert = 0,

    /// <summary>Silent background notification dispatched at normal priority.</summary>
    Background = 1,

    /// <summary>
    /// PushKit VoIP notification dispatched to the <c>.voip</c> topic.
    /// </summary>
    Voip = 2,

    /// <summary>Live Activity notification dispatched to the <c>.push-type.liveactivity</c> topic.</summary>
    LiveActivity = 3,

    /// <summary>Location query notification dispatched to the <c>.location-query</c> topic.</summary>
    Location = 4,

    /// <summary>
    /// Push-to-Talk notification dispatched to the <c>.voip-ptt</c> topic.
    /// </summary>
    PushToTalk = 5,

    /// <summary>WidgetKit reload notification dispatched to the <c>.push-type.widgets</c> topic.</summary>
    Widgets = 6,

    /// <summary>Control center reload notification dispatched to the <c>.push-type.controls</c> topic.</summary>
    Controls = 7,

    /// <summary>ClockKit complication notification dispatched to the <c>.complication</c> topic.</summary>
    Complication = 8,

    /// <summary>File Provider sync notification dispatched to the <c>.pushkit.fileprovider</c> topic.</summary>
    FileProvider = 9,
}
