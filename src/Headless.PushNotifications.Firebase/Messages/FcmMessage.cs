// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// A Firebase Cloud Messaging (FCM) v1 message: the cross-platform notification and data, plus the Android, web
/// push, and APNs overrides the provider-neutral <see cref="PushNotificationRequest"/> cannot express.
/// </summary>
/// <remarks>
/// <para>
/// Every block is optional. A message with no <see cref="Notification"/> is a data message, which the app handles
/// itself. The platform blocks override the shared fields for their platform only, as FCM merges them.
/// </para>
/// <para>
/// Invalid input throws <see cref="ArgumentException"/> before any request is sent. Limits FCM enforces itself, such
/// as the 4096-byte payload (2048 bytes for a topic send), are left to FCM, which rejects an oversized message as
/// <see cref="FcmFailureKind.Payload"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record FcmMessage
{
    /// <summary>The notification every platform shows; <see langword="null"/> for a data message.</summary>
    public FcmNotification? Notification { get; init; }

    /// <summary>
    /// Custom key-value data delivered to the app. Keys must not be <c>from</c>, <c>notification</c>,
    /// <c>message_type</c>, or start with <c>google.</c> or <c>gcm.</c>, which FCM reserves.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>Android-specific delivery and notification options.</summary>
    public FcmAndroidOptions? Android { get; init; }

    /// <summary>Web push options.</summary>
    public FcmWebpushOptions? Webpush { get; init; }

    /// <summary>APNs headers and payload for the iOS bridge.</summary>
    public FcmApnsOptions? Apns { get; init; }

    /// <summary>
    /// The label FCM attaches to the message in its delivery analytics (<c>fcm_options.analytics_label</c>). Must
    /// match <c>^[a-zA-Z0-9-_.~%]{1,50}$</c>.
    /// </summary>
    public string? AnalyticsLabel { get; init; }

    /// <summary>
    /// When <see langword="true"/>, FCM validates the message (<c>validate_only</c>) without delivering it. A valid
    /// message succeeds with a placeholder message id.
    /// </summary>
    public bool DryRun { get; init; }
}
