// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Represents a data-only APNs notification payload configured for VoIP delivery.
/// </summary>
internal sealed record ApnsVoipDataNotification : ApnsNotification
{
    /// <summary>Gets the custom JSON properties written alongside the <c>aps</c> dictionary.</summary>
    public JsonObject? Data { get; init; }

    /// <summary>Gets the delivery priority, or <see langword="null"/> to apply instance options default.</summary>
    public ApnsPriority? Priority { get; init; }
}
