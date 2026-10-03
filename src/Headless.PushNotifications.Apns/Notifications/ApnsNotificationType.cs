// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The APNs push type of an <see cref="ApnsRawNotification"/>, sent as the <c>apns-push-type</c> header. Each value
/// applies the same topic, priority, size, and authentication rules as the typed notification of that push type.
/// </summary>
[PublicAPI]
public enum ApnsNotificationType
{
    /// <summary>
    /// <c>alert</c>: a user-visible notification, sent to the bundle id topic. Sent as <c>voip</c> through an
    /// instance configured with <see cref="ApnsPushType.Voip"/>.
    /// </summary>
    Alert = 0,

    /// <summary><c>background</c>: a silent push, sent to the bundle id topic at priority 5 only.</summary>
    Background = 1,

    /// <summary>
    /// <c>voip</c>: a PushKit VoIP push, sent to the <c>&lt;bundle&gt;.voip</c> topic with a 5 KB payload limit. Needs an
    /// instance configured with <see cref="ApnsPushType.Voip"/>.
    /// </summary>
    Voip = 2,

    /// <summary><c>liveactivity</c>: a Live Activity push, sent to the <c>&lt;bundle&gt;.push-type.liveactivity</c> topic.</summary>
    LiveActivity = 3,

    /// <summary><c>location</c>: a location query, sent to the <c>&lt;bundle&gt;.location-query</c> topic.</summary>
    Location = 4,

    /// <summary>
    /// <c>pushtotalk</c>: a PushToTalk push, sent to the <c>&lt;bundle&gt;.voip-ptt</c> topic at priority 10 only and
    /// delivered once unless an expiration is set.
    /// </summary>
    PushToTalk = 5,

    /// <summary><c>widgets</c>: a WidgetKit reload, sent to the <c>&lt;bundle&gt;.push-type.widgets</c> topic.</summary>
    Widgets = 6,

    /// <summary><c>controls</c>: a controls reload, sent to the <c>&lt;bundle&gt;.push-type.controls</c> topic.</summary>
    Controls = 7,

    /// <summary><c>complication</c>: a ClockKit complication update, sent to the <c>&lt;bundle&gt;.complication</c> topic.</summary>
    Complication = 8,

    /// <summary><c>fileprovider</c>: a File Provider sync signal, sent to the <c>&lt;bundle&gt;.pushkit.fileprovider</c> topic.</summary>
    FileProvider = 9,
}
