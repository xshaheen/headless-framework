// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Defines operations for sending push notifications to client devices across push notification providers.
/// </summary>
/// <remarks>
/// Per-client delivery errors are returned in the response rather than thrown as exceptions.
/// Callers should inspect <see cref="PushNotificationResponse.Status"/> to handle failures and remove
/// unregistered identifiers from storage.
/// </remarks>
[PublicAPI]
public interface IPushNotificationService
{
    /// <summary>
    /// Sends a push notification to a single device.
    /// </summary>
    /// <param name="clientIdentifier">The device identifier issued by the push notification provider.</param>
    /// <param name="request">The notification request to deliver.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A response describing the delivery outcome for <paramref name="clientIdentifier"/>.</returns>
    ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientIdentifier,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a push notification to multiple devices.
    /// </summary>
    /// <param name="clientIdentifiers">The provider-issued device identifiers to deliver to.</param>
    /// <param name="request">The notification request to deliver.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// An aggregate response containing individual outcomes and overall counts.
    /// </returns>
    ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    );
}
