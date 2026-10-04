// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Firebase;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>Web push options of an <see cref="FcmMessage"/>, FCM's <c>webpush</c> block.</summary>
[PublicAPI]
public sealed record FcmWebpushOptions
{
    /// <summary>The absolute HTTPS URL the browser opens when the user clicks the notification.</summary>
    public Uri? Link { get; init; }

    /// <summary>Web push protocol headers, such as <c>TTL</c> or <c>Urgency</c>.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Data for web clients only, overriding <see cref="FcmMessage.Data"/> there.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }
}

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
