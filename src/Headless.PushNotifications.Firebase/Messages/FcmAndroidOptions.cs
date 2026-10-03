// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Android options of an <see cref="FcmMessage"/>: the delivery settings of FCM's <c>android</c> block and the
/// fields of its <c>android.notification</c> block.
/// </summary>
/// <remarks>
/// The notification fields are sent only when at least one is set, so a data message stays a data message on
/// Android unless one of them turns it into a notification.
/// </remarks>
[PublicAPI]
public sealed record FcmAndroidOptions
{
    /// <summary>The delivery priority; <see langword="null"/> leaves FCM's default (normal).</summary>
    public FcmAndroidPriority? Priority { get; init; }

    /// <summary>
    /// How long FCM stores the message while the device is offline, from zero (deliver now or drop) to 28 days;
    /// <see langword="null"/> leaves FCM's default of 28 days.
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>
    /// Groups messages so FCM delivers only the latest one with the same key when the device comes back online.
    /// </summary>
    public string? CollapseKey { get; init; }

    /// <summary>Whether the message may be delivered while the device is in direct boot mode.</summary>
    public bool? DirectBootOk { get; init; }

    /// <summary>The Android notification channel the notification posts to.</summary>
    public string? ChannelId { get; init; }

    /// <summary>Replaces an existing notification with the same tag in the notification drawer.</summary>
    public string? Tag { get; init; }

    /// <summary>The notification icon color in <c>#RRGGBB</c> form.</summary>
    public string? Color { get; init; }

    /// <summary>The drawable resource name of the notification icon.</summary>
    public string? Icon { get; init; }

    /// <summary>The intent filter action the notification opens when tapped.</summary>
    public string? ClickAction { get; init; }

    /// <summary>The sound to play: "default" for the device sound, or the name of a sound resource in <c>res/raw</c>.</summary>
    public string? Sound { get; init; }

    /// <summary>The count the launcher badge shows for this notification; zero or more.</summary>
    public int? NotificationCount { get; init; }

    /// <summary>How the notification shows on a locked screen; <see langword="null"/> leaves Android's default.</summary>
    public FcmAndroidVisibility? Visibility { get; init; }

    /// <summary>An absolute URL of an image the Android notification shows, overriding <see cref="FcmNotification.Image"/>.</summary>
    public Uri? Image { get; init; }
}
