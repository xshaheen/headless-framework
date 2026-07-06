// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Describes a single push notification to deliver via <see cref="IPushNotificationService"/>.
/// </summary>
/// <remarks>
/// The same request is used for single-device and multicast sends; only the target token(s) differ.
/// New delivery options (image, sound, badge, priority, TTL, collapse key, click action, …) are added as
/// optional <c>init</c> properties so the contract can grow without changing the method signatures.
/// </remarks>
[PublicAPI]
public sealed record PushNotificationRequest
{
    /// <summary>The notification title shown to the user.</summary>
    public required string Title { get; init; }

    /// <summary>The notification body shown to the user.</summary>
    public required string Body { get; init; }

    /// <summary>
    /// Optional custom key/value payload delivered alongside the notification. Keys reserved by the
    /// underlying provider may be rejected by the implementation. Defaults to <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }
}
