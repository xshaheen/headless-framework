// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Describes a single push notification to deliver through <see cref="IPushNotificationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Single-device and multicast sends share this request type; only target client identifiers differ.
/// Optional properties default to <see langword="null"/>.
/// </para>
/// <para>
/// A request must match one of two configurations:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A notification message specifies a non-blank <see cref="Title"/> and <see cref="Body"/>, and displays to the user.
/// </description></item>
/// <item><description>
/// A data-only message specifies no <see cref="Title"/>, no <see cref="Body"/>, at least one <see cref="Data"/> entry,
/// and no <see cref="Badge"/> or <see cref="Sound"/>. It wakes the application in the background.
/// </description></item>
/// </list>
/// </remarks>
[PublicAPI]
public sealed record PushNotificationRequest
{
    /// <summary>Gets the notification title displayed to the user, or <see langword="null"/> for a data-only message.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the notification body displayed to the user, or <see langword="null"/> for a data-only message.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// Gets the custom key-value payload delivered with the notification, or the complete payload of a data-only
    /// message. Defaults to <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>
    /// Gets the identifier used to collapse multiple notifications into the newest message on the device.
    /// Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// APNs maps this value to the <c>apns-collapse-id</c> header, capped at 64 UTF-8 bytes.
    /// Firebase maps this value to the Android collapse key and the APNs header.
    /// </remarks>
    public string? CollapseKey { get; init; }

    /// <summary>
    /// Gets the application icon badge count. Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A value of 0 clears the badge on iOS. This property is not permitted on data-only messages.
    /// </remarks>
    public int? Badge { get; init; }

    /// <summary>
    /// Gets the sound to play on delivery. Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Values specify a bundled sound filename or <c>"default"</c> for the system sound.
    /// This property is not permitted on data-only messages.
    /// </remarks>
    public string? Sound { get; init; }

    /// <summary>
    /// Gets the delivery priority. Defaults to <see langword="null"/>, which applies the provider default.
    /// </summary>
    /// <remarks>
    /// APNs requires normal priority for background push notifications and ignores <see cref="PushNotificationPriority.High"/>
    /// for data-only messages.
    /// </remarks>
    public PushNotificationPriority? Priority { get; init; }

    /// <summary>
    /// Gets the storage duration for offline devices, between zero and 28 days.
    /// Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> indicates one delivery attempt without storage.
    /// </remarks>
    public TimeSpan? TimeToLive { get; init; }
}
