// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// An APNs notification. Each sealed subtype is one APNs push type and fixes that type's <c>apns-push-type</c>
/// header, topic, priority rules, and payload fields.
/// </summary>
/// <remarks>
/// The hierarchy is closed to this assembly, so a notification's push type is decided by its type rather than
/// checked at send time: an alert on a background push cannot be expressed.
/// </remarks>
[PublicAPI]
public abstract record ApnsNotification
{
    // private protected rather than private: a private constructor is unreachable from top-level subtypes.
    private protected ApnsNotification() { }

    /// <summary>
    /// When APNs stops trying to deliver the notification, sent as <c>apns-expiration</c>. Default:
    /// <see langword="null"/>, which sends no header so APNs applies its own storage policy, except on VoIP and
    /// push-to-talk pushes, where it sends <see cref="ApnsExpiration.DeliverOnce"/> as Apple instructs.
    /// </summary>
    public ApnsExpiration? Expiration { get; init; }

    /// <summary>
    /// The identifier that merges notifications into one on the device, sent as <c>apns-collapse-id</c>. At most 64
    /// UTF-8 bytes.
    /// </summary>
    public string? CollapseId { get; init; }

    /// <summary>
    /// The identifier sent as <c>apns-id</c> and returned as <see cref="ApnsSendResult.ApnsId"/>. Default:
    /// <see langword="null"/>, which sends a new random UUID. Set it to correlate the send with your own records; a
    /// resend after an expired provider token keeps it.
    /// </summary>
    /// <remarks>Only a single-token send accepts it: a multicast refuses it, because every request needs its own id.</remarks>
    public Guid? ApnsId { get; init; }
}
