// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Provider-agnostic contract for sending push notifications to client devices.
/// </summary>
/// <remarks>
/// Implementations target a specific backend (for example Firebase Cloud Messaging). Per-token delivery
/// problems are reported through the returned response rather than thrown, so callers should inspect
/// <see cref="PushNotificationResponse.Status"/> to react to failures and, in particular, to detect tokens
/// that are no longer registered and should be removed from their store.
/// </remarks>
public interface IPushNotificationService
{
    /// <summary>
    /// Sends a single notification to one device identified by its registration token.
    /// </summary>
    /// <param name="clientToken">The device registration token issued by the push provider.</param>
    /// <param name="request">The notification to deliver (title, body, and optional data payload).</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <returns>
    /// A response describing the outcome for <paramref name="clientToken"/>: delivered (with a provider
    /// message id), failed (with an error description), or unregistered when the token is no longer valid.
    /// </returns>
    ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends the same notification to many devices in a single call.
    /// </summary>
    /// <param name="clientTokens">The device registration tokens to deliver to.</param>
    /// <param name="request">The notification to deliver (title, body, and optional data payload).</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <returns>
    /// An aggregate response carrying one per-token outcome for every entry in <paramref name="clientTokens"/>
    /// plus overall success and failure counts.
    /// </returns>
    /// <remarks>
    /// Implementations may transparently split large token lists into provider-sized batches. Whether a
    /// whole-call transport failure is thrown or surfaced as failed per-token responses is
    /// implementation-specific.
    /// </remarks>
    ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientTokens,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    );
}
