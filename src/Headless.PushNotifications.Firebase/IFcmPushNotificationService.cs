// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Sends FCM-native messages, with the Android, web push, and APNs options the shared request cannot express, to
/// devices, topics, and conditions, and returns the FCM details of each outcome.
/// </summary>
/// <remarks>
/// <para>
/// Resolvable wherever the Firebase <see cref="IPushNotificationService"/> is: unkeyed for the default instance,
/// keyed by name for a named instance. Both service types resolve to the same instance, which shares one Firebase
/// app, credentials, retry policy, and options.
/// </para>
/// <para>
/// Per-target outcomes never throw: FCM rejections, transport failures, timeouts, and credential failures all
/// become a <see cref="FcmSendResult"/>. Only invalid input and caller cancellation throw, and invalid input throws
/// before any request is sent. Every send retries <c>INTERNAL</c> and <c>QUOTA_EXCEEDED</c> per
/// <see cref="FirebaseRetryOptions"/>, on top of the FirebaseAdmin SDK's own 503 and network retries.
/// </para>
/// </remarks>
[PublicAPI]
public interface IFcmPushNotificationService
{
    /// <summary>Sends <paramref name="message"/> to one device.</summary>
    /// <param name="fid">The device's Firebase Installation ID (FID) or registration token.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The outcome for <paramref name="fid"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="fid"/> is blank, or <paramref name="message"/> is invalid.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<FcmSendResult> SendAsync(string fid, FcmMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="message"/> to every device in <paramref name="fids"/>, in batches of at most 500, and
    /// resends only the devices that failed with a retried code.
    /// </summary>
    /// <param name="fids">The devices' FIDs or registration tokens; each must be non-blank.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancels the multicast.</param>
    /// <returns>One outcome per device, in input order.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fids"/> is empty or holds a blank entry, or <paramref name="message"/> is invalid.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<FcmBatchSendResult> SendMulticastAsync(
        IReadOnlyList<string> fids,
        FcmMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <paramref name="message"/> to every device subscribed to <paramref name="topic"/>.</summary>
    /// <param name="topic">
    /// The topic name without the <c>/topics/</c> prefix, matching <c>[a-zA-Z0-9-_.~%]+</c>.
    /// </param>
    /// <param name="message">The message to send. FCM limits a topic message to 2048 bytes.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The outcome of the send, whose client identifier is <paramref name="topic"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="topic"/> is blank, carries the <c>/topics/</c> prefix, or holds a character FCM does not
    /// allow, or <paramref name="message"/> is invalid.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<FcmSendResult> SendToTopicAsync(
        string topic,
        FcmMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <paramref name="message"/> to every device whose topic subscriptions satisfy <paramref name="condition"/>.</summary>
    /// <param name="condition">
    /// An FCM condition such as <c>'news' in topics &amp;&amp; ('sports' in topics || 'tech' in topics)</c>,
    /// naming between one and five topics.
    /// </param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The outcome of the send, whose client identifier is <paramref name="condition"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="condition"/> is blank, names no topic or more than five, or names a topic with a character
    /// FCM does not allow, or <paramref name="message"/> is invalid.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<FcmSendResult> SendToConditionAsync(
        string condition,
        FcmMessage message,
        CancellationToken cancellationToken = default
    );
}
