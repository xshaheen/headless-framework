// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// APNs options of an <see cref="FcmMessage"/>, FCM's <c>apns</c> block, which FCM forwards to Apple for iOS
/// devices.
/// </summary>
[PublicAPI]
public sealed record FcmApnsOptions
{
    /// <summary>APNs request headers, such as <c>apns-priority</c>, <c>apns-expiration</c>, or <c>apns-collapse-id</c>.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// The full APNs payload: an <c>aps</c> object plus any custom top-level keys, sent as written. It must hold an
    /// <c>aps</c> object, which may be empty, because FCM requires one. FCM merges <see cref="FcmMessage.Notification"/>
    /// into <c>aps.alert</c>.
    /// </summary>
    /// <remarks>The payload is copied when the message is sent, so changing it afterwards does not affect that send.</remarks>
    public JsonObject? Payload { get; init; }
}
