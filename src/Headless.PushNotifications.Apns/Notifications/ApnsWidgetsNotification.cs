// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// A push that tells WidgetKit the app's widgets have new content to reload. Sent with push type <c>widgets</c> to
/// the <c>&lt;bundle&gt;.push-type.widgets</c> topic, always written as <c>{"aps":{"content-changed":true}}</c>.
/// </summary>
/// <remarks>
/// WidgetKit also serves the watch complications that replace ClockKit, so this push type reloads them too. A VoIP
/// instance refuses this push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsWidgetsNotification : ApnsNotification
{
    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}
