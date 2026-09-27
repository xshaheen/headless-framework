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

    /// <summary>
    /// Broadcasts a Live Activity update or end to every device subscribed to <paramref name="channelId"/>, with one
    /// request (iOS 18 and iPadOS 18 or later).
    /// </summary>
    /// <param name="channelId">The channel id from <see cref="IApnsBroadcastChannelService.CreateAsync"/>.</param>
    /// <param name="notification">
    /// A Live Activity <see cref="ApnsLiveActivityEvent.Update"/> or <see cref="ApnsLiveActivityEvent.End"/>. Apple does
    /// not let a broadcast start an activity; start it on each device with
    /// <see cref="ApnsLiveActivityNotification.InputPushChannel"/> set instead.
    /// </param>
    /// <param name="cancellationToken">Cancels the broadcast.</param>
    /// <returns>
    /// APNs' answer for the broadcast as a whole. A rejection or transport fault is a failed result, not an
    /// exception.
    /// </returns>
    /// <remarks>
    /// The request always carries <c>apns-expiration</c>, which Apple requires on a broadcast: a
    /// <see langword="null"/> <see cref="ApnsNotification.Expiration"/> sends <c>0</c> (deliver once, never store).
    /// A nonzero expiration on a channel created with <see cref="ApnsChannelStoragePolicy.NoMessageStored"/> is
    /// rejected by APNs. The payload limit is 5120 bytes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="channelId"/> is blank, or <paramref name="notification"/> starts an activity, sets start-only
    /// fields or a collapse id, breaks a Live Activity rule, or is over the payload limit.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ApnsBroadcastResult> SendBroadcastAsync(
        string channelId,
        ApnsLiveActivityNotification notification,
        CancellationToken cancellationToken = default
    );
}
