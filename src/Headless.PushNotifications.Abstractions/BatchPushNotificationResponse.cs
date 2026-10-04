// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Represents the aggregate result of a multicast send, combining overall counts with per-client outcomes.
/// </summary>
[PublicAPI]
public sealed class BatchPushNotificationResponse
{
    /// <summary>Gets the number of client identifiers the provider accepted for delivery.</summary>
    public required int SuccessCount { get; init; }

    /// <summary>
    /// Gets the number of client identifiers the provider did not accept. Identifiers reported as
    /// <see cref="PushNotificationResponseStatus.Unregistered"/> are included in this count.
    /// </summary>
    public required int FailureCount { get; init; }

    /// <summary>
    /// Gets the outcome for each requested client identifier.
    /// </summary>
    public required IReadOnlyList<PushNotificationResponse> Responses { get; init; }
}
