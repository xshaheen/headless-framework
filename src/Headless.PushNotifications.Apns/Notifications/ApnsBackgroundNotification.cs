// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// A silent push that wakes the app in the background, written as <c>aps.content-available: 1</c> plus custom data.
/// Sent with push type <c>background</c> at priority 5, which Apple requires for this push type.
/// </summary>
/// <remarks>
/// The system throttles background pushes and may drop them, so do not rely on one arriving. A VoIP instance
/// refuses it, because PushKit tokens cannot receive background pushes.
/// </remarks>
[PublicAPI]
public sealed record ApnsBackgroundNotification : ApnsNotification
{
    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }
}
