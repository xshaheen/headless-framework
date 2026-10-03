// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// A push that updates a ClockKit watch complication. Sent with push type <c>complication</c> to the
/// <c>&lt;bundle&gt;.complication</c> topic, written as an empty <c>aps</c> dictionary plus custom data.
/// </summary>
/// <remarks>
/// WidgetKit supersedes ClockKit, and <see cref="ApnsWidgetsNotification"/> reloads widget-based complications.
/// Apple's reference renders this topic's suffix as <c>h.complication</c>, which reads as a typo for
/// <c>.complication</c>; a <c>TopicDisallowed</c> rejection points at that discrepancy. A VoIP instance refuses this
/// push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsComplicationNotification : ApnsNotification
{
    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}
