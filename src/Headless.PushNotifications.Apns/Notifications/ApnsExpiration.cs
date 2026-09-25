// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications.Apns;

/// <summary>
/// When APNs stops trying to deliver a notification, sent as the <c>apns-expiration</c> header: either an absolute
/// instant, or <see cref="DeliverOnce"/>.
/// </summary>
/// <remarks>Without an expiration APNs applies its own storage policy.</remarks>
[PublicAPI]
public sealed record ApnsExpiration
{
    private ApnsExpiration(DateTimeOffset? expiresAt)
    {
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Attempt delivery once and do not store the notification; an offline device never receives it. Sent as
    /// <c>apns-expiration: 0</c>.
    /// </summary>
    public static ApnsExpiration DeliverOnce { get; } = new(expiresAt: null);

    /// <summary>The instant after which APNs discards the notification, or <see langword="null"/> for <see cref="DeliverOnce"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Whether this is <see cref="DeliverOnce"/>.</summary>
    public bool IsDeliverOnce => ExpiresAt is null;

    /// <summary>Creates an expiration at <paramref name="expiresAt"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="expiresAt"/> is not after the Unix epoch. APNs reads <c>0</c> as deliver-once, so an earlier
    /// instant cannot be expressed.
    /// </exception>
    public static ApnsExpiration At(DateTimeOffset expiresAt)
    {
        Argument.IsGreaterThan(expiresAt, DateTimeOffset.UnixEpoch);

        return new ApnsExpiration(expiresAt);
    }
}
