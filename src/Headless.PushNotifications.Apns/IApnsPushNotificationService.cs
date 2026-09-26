// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Sends APNs-native notifications (every typed push type, or a raw payload) and returns the APNs details of each
/// outcome.
/// </summary>
/// <remarks>
/// <para>
/// Resolvable wherever the APNs <see cref="IPushNotificationService"/> is: unkeyed for the default instance, keyed
/// by name for a named instance. Both service types resolve to the same instance, which shares one HTTP client,
/// provider token, and options.
/// </para>
/// <para>
/// Per-token outcomes never throw: rejections, transport faults left after retries, and resilience rejections all
/// become a failed <see cref="ApnsSendResult.Response"/>. Only invalid input and caller cancellation throw, and
/// invalid input throws before any request is sent.
/// </para>
/// </remarks>
[PublicAPI]
public interface IApnsPushNotificationService
{
    /// <summary>Sends <paramref name="notification"/> to one device token.</summary>
    /// <param name="deviceToken">The device token, or for a Live Activity the activity's push or push-to-start token.</param>
    /// <param name="notification">The notification to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>
    /// The outcome for <paramref name="deviceToken"/>, whose <see cref="ApnsSendResult.ApnsId"/> is
    /// <see cref="ApnsNotification.ApnsId"/> when the notification sets one.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="deviceToken"/> is blank, or <paramref name="notification"/> breaks a rule of its push type or
    /// cannot be sent through this instance.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ApnsSendResult> SendAsync(
        string deviceToken,
        ApnsNotification notification,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <paramref name="notification"/> to every token in <paramref name="deviceTokens"/>.</summary>
    /// <param name="deviceTokens">The device tokens; each must be non-blank.</param>
    /// <param name="notification">The notification to send.</param>
    /// <param name="cancellationToken">Cancels the multicast.</param>
    /// <returns>One outcome per token, in input order.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="deviceTokens"/> is empty or holds a blank token, <paramref name="notification"/> sets
    /// <see cref="ApnsNotification.ApnsId"/>, or <paramref name="notification"/> breaks a rule of its push type or
    /// cannot be sent through this instance.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ApnsBatchSendResult> SendMulticastAsync(
        IReadOnlyList<string> deviceTokens,
        ApnsNotification notification,
        CancellationToken cancellationToken = default
    );
}
