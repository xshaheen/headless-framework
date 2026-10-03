// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Firebase;

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
