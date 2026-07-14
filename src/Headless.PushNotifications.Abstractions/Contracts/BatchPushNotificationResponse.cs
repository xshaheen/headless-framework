// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Aggregate result of a multicast send, combining overall counts with per-token outcomes.
/// </summary>
[PublicAPI]
public sealed class BatchPushNotificationResponse
{
    /// <summary>Number of tokens the provider accepted for delivery.</summary>
    public required int SuccessCount { get; init; }

    /// <summary>
    /// Number of tokens the provider did not accept. Tokens reported as
    /// <see cref="PushNotificationResponseStatus.Unregistered"/> are included in this count.
    /// </summary>
    public required int FailureCount { get; init; }

    /// <summary>
    /// One outcome per requested token. Inspect these to act on individual results — for example to delete
    /// tokens whose <see cref="PushNotificationResponse.Status"/> is
    /// <see cref="PushNotificationResponseStatus.Unregistered"/>.
    /// </summary>
    public required IReadOnlyList<PushNotificationResponse> Responses { get; init; }
}
