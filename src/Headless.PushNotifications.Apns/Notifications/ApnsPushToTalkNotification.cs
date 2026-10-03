// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// A push that tells the app's PushToTalk channel about new audio or a change of speaker. Sent with push type
/// <c>pushtotalk</c> to the <c>&lt;bundle&gt;.voip-ptt</c> topic at priority 10, written as an empty <c>aps</c>
/// dictionary plus custom data.
/// </summary>
/// <remarks>
/// A stale push-to-talk push is worse than none, so a <see langword="null"/> <see cref="ApnsNotification.Expiration"/>
/// sends <see cref="ApnsExpiration.DeliverOnce"/> rather than letting APNs store it; set an explicit expiration to
/// override. The token is the one the PushToTalk framework reports, not a PushKit VoIP token, so a VoIP instance
/// refuses this push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsPushToTalkNotification : ApnsNotification
{
    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }
}
