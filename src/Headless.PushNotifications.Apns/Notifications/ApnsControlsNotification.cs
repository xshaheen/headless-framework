// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// A push that tells the system the app's controls have new state to reload. Sent with push type <c>controls</c> to
/// the <c>&lt;bundle&gt;.push-type.controls</c> topic, always written as <c>{"aps":{"content-changed":true}}</c>.
/// </summary>
/// <remarks>A VoIP instance refuses this push type.</remarks>
[PublicAPI]
public sealed record ApnsControlsNotification : ApnsNotification
{
    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}
