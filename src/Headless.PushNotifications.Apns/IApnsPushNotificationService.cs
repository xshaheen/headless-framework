// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Defines APNs-specific push notification operations, including typed payloads and channel broadcasts.
/// </summary>
[PublicAPI]
public interface IApnsPushNotificationService
{
    /// <summary>Sends an APNs notification to a single device token.</summary>
    /// <param name="deviceToken">The target device token or Live Activity push token.</param>
    /// <param name="notification">The APNs notification to send.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The delivery outcome for <paramref name="deviceToken"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="deviceToken"/> is empty or white space, or <paramref name="notification"/> violates constraints for its push type.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ApnsSendResult> SendAsync(
        string deviceToken,
        ApnsNotification notification,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends an APNs notification to multiple device tokens.</summary>
    /// <param name="deviceTokens">The collection of device tokens to deliver to.</param>
    /// <param name="notification">The APNs notification to send.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A batch send result containing individual outcomes in input order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="deviceTokens"/> or <paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="deviceTokens"/> is empty or contains an empty token, <paramref name="notification"/> sets <see cref="ApnsNotification.ApnsId"/>,
    /// or <paramref name="notification"/> violates constraints for its push type.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ApnsBatchSendResult> SendMulticastAsync(
        IReadOnlyList<string> deviceTokens,
        ApnsNotification notification,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Broadcasts a Live Activity update or termination event to an APNs broadcast channel.
    /// </summary>
    /// <param name="channelId">The target broadcast channel identifier.</param>
    /// <param name="notification">The Live Activity notification to broadcast.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The broadcast delivery outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="channelId"/> is empty or white space, or <paramref name="notification"/> specifies an invalid broadcast configuration.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ApnsBroadcastResult> SendBroadcastAsync(
        string channelId,
        ApnsLiveActivityNotification notification,
        CancellationToken cancellationToken = default
    );
}
