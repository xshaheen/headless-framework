// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Describes a single push notification to deliver via <see cref="IPushNotificationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The same request is used for single-device and multicast sends; only the target client identifiers differ.
/// New delivery options are added as optional <c>init</c> properties so the contract can grow without changing the
/// method signatures. <see langword="null"/> means "not set" for every optional property.
/// </para>
/// <para>
/// A request is one of two kinds, and providers reject any other combination with
/// <see cref="ArgumentException"/> before contacting the backend:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A <b>notification</b> has a non-blank <see cref="Title"/> and <see cref="Body"/>, and is shown to the user.
/// </description></item>
/// <item><description>
/// A <b>data-only</b> message has no <see cref="Title"/>, no <see cref="Body"/>, at least one <see cref="Data"/>
/// entry, and no <see cref="Badge"/> or <see cref="Sound"/>. It wakes the app in the background instead of showing
/// anything, so the device may throttle or drop it.
/// </description></item>
/// </list>
/// </remarks>
[PublicAPI]
public sealed record PushNotificationRequest
{
    /// <summary>The notification title shown to the user; <see langword="null"/> for a data-only message.</summary>
    public string? Title { get; init; }

    /// <summary>The notification body shown to the user; <see langword="null"/> for a data-only message.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// Optional custom key/value payload delivered alongside the notification, and the whole payload of a data-only
    /// message. Keys reserved by the underlying provider may be rejected by the implementation. Defaults to
    /// <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>
    /// Optional key that groups notifications so a newer one replaces an older undelivered one with the same key
    /// on the device. Defaults to <see langword="null"/> (no collapsing).
    /// </summary>
    /// <remarks>
    /// Each provider maps and limits the key: APNs sends it as the <c>apns-collapse-id</c> header, which Apple
    /// caps at 64 UTF-8 bytes, and Firebase sends it as the Android collapse key and as the same APNs header
    /// through its iOS bridge. Providers reject a key over their limit with <see cref="ArgumentException"/>.
    /// </remarks>
    public string? CollapseKey { get; init; }

    /// <summary>
    /// Optional app icon badge count; must not be negative. Defaults to <see langword="null"/> (the badge is left
    /// unchanged).
    /// </summary>
    /// <remarks>
    /// <c>0</c> clears the badge on iOS. Android has no way to clear it, so there <c>0</c> is treated as not set.
    /// Not allowed on a data-only message.
    /// </remarks>
    public int? Badge { get; init; }

    /// <summary>
    /// Optional sound to play: the name of a sound file bundled with the app, or <c>"default"</c> for the system
    /// sound. Defaults to <see langword="null"/> (silent). Not allowed on a data-only message.
    /// </summary>
    public string? Sound { get; init; }

    /// <summary>
    /// Optional delivery priority. Defaults to <see langword="null"/>, which applies the provider's default
    /// priority.
    /// </summary>
    /// <remarks>
    /// Apple requires priority 5 for a background push, so APNs delivery of a data-only message ignores
    /// <see cref="PushNotificationPriority.High"/>.
    /// </remarks>
    public PushNotificationPriority? Priority { get; init; }

    /// <summary>
    /// Optional time the provider keeps the message for a device that is offline; must not be negative. Defaults to
    /// <see langword="null"/>, which leaves the provider's own storage policy in place.
    /// </summary>
    /// <remarks><see cref="TimeSpan.Zero"/> means one delivery attempt with no storage.</remarks>
    public TimeSpan? TimeToLive { get; init; }
}
