// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// A push that asks the app's Location Push Service Extension for the device's location. Sent with push type
/// <c>location</c> to the <c>&lt;bundle&gt;.location-query</c> topic, written as an empty <c>aps</c> dictionary plus
/// custom data.
/// </summary>
/// <remarks>
/// Apple documents no payload for this push type, so the extension receives only the custom data. A VoIP instance
/// refuses this push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsLocationNotification : ApnsNotification
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
