// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Describes a single push notification to deliver through <see cref="IPushNotificationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Single-device and multicast sends share this request type; only the target client identifiers differ. New
/// delivery options are added as optional <see langword="init"/> properties so the contract can grow without
/// changing the method signatures. <see langword="null"/> means "not set" for every optional property.
/// </para>
/// <para>
/// A request must match one of two configurations, and providers reject any other combination with
/// <see cref="ArgumentException"/> before contacting the backend:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A notification message specifies a non-blank <see cref="Title"/> and <see cref="Body"/>, and displays to the user.
/// </description></item>
/// <item><description>
/// A data-only message specifies no <see cref="Title"/>, no <see cref="Body"/>, at least one <see cref="Data"/>
/// entry, and no <see cref="Badge"/> or <see cref="Sound"/>. It wakes the application in the background instead of
/// showing anything, so the device may throttle or drop it.
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
    /// <remarks>
    /// Keys reserved by the underlying provider may be rejected by the implementation.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>
    /// Gets the identifier used to collapse multiple notifications into the newest message on the device.
    /// Defaults to <see langword="null"/>, which disables collapsing.
    /// </summary>
    /// <remarks>
    /// Each provider maps and limits the key: APNs maps this value to the <c>apns-collapse-id</c> header, which
    /// Apple caps at 64 UTF-8 bytes, and Firebase maps it to the Android collapse key and to the same APNs header
    /// through its iOS bridge. Providers reject a key over their limit with <see cref="ArgumentException"/>.
    /// </remarks>
    public string? CollapseKey { get; init; }

    /// <summary>
    /// Gets the application icon badge count, which must not be negative. Defaults to <see langword="null"/>,
    /// which leaves the badge unchanged.
    /// </summary>
    /// <remarks>
    /// A value of 0 clears the badge on iOS. Android has no way to clear it, so there 0 is treated as not set.
    /// This property is not permitted on data-only messages.
    /// </remarks>
    public int? Badge { get; init; }

    /// <summary>
    /// Gets the sound to play on delivery. Defaults to <see langword="null"/>, which delivers silently.
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
    /// Apple requires priority 5 for a background push, so APNs delivery of a data-only message ignores
    /// <see cref="PushNotificationPriority.High"/>.
    /// </remarks>
    public PushNotificationPriority? Priority { get; init; }

    /// <summary>
    /// Gets the storage duration for offline devices, which must be between zero and 28 days. Defaults to
    /// <see langword="null"/>, which leaves the provider's own storage policy in place.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> indicates one delivery attempt without storage. 28 days is Firebase's maximum
    /// Android time-to-live, so every provider rejects a longer value with
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    public TimeSpan? TimeToLive { get; init; }
}
