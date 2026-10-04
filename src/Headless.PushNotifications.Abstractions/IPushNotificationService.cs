// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Defines operations for sending push notifications to client devices across push notification providers.
/// </summary>
/// <remarks>
/// Implementations target a specific backend, for example Firebase Cloud Messaging. Per-client delivery
/// problems are returned in the response rather than thrown, so callers should inspect
/// <see cref="PushNotificationResponse.Status"/> to react to failures and, in particular, to detect client
/// identifiers that are no longer registered and should be removed from their store.
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
    /// <returns>
    /// A response describing the outcome for <paramref name="clientIdentifier"/>: delivered (with a provider
    /// message id), failed (with an error description), or unregistered when the identifier is no longer valid.
    /// </returns>
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
    /// An aggregate response carrying one outcome for every entry in <paramref name="clientIdentifiers"/>
    /// plus overall success and failure counts.
    /// </returns>
    /// <remarks>
    /// Implementations may transparently split large identifier lists into provider-sized batches. Whether a
    /// whole-call transport failure is thrown or surfaced as failed per-client responses is
    /// implementation-specific.
    /// </remarks>
    ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    );
}
