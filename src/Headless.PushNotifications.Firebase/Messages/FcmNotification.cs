// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>The notification FCM shows on every platform unless a platform block overrides it.</summary>
[PublicAPI]
public sealed record FcmNotification
{
    /// <summary>The notification title.</summary>
    public string? Title { get; init; }

    /// <summary>The notification body text.</summary>
    public string? Body { get; init; }

    /// <summary>An absolute URL of an image the notification shows.</summary>
    public Uri? Image { get; init; }
}
