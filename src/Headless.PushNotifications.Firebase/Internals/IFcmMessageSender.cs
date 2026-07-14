// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>The notification content sent to one or more device tokens.</summary>
internal sealed record FcmMessageContent(string Title, string Body, IReadOnlyDictionary<string, string>? Data);

/// <summary>
/// Seam over Firebase Cloud Messaging. Owns all <c>FirebaseAdmin</c> interaction (app lifecycle, message
/// construction, transient-error retry, and outcome classification) and returns provider-agnostic
/// <see cref="PushNotificationResponse"/> values, keeping <see cref="FcmPushNotificationService"/> testable.
/// </summary>
internal interface IFcmMessageSender
{
    /// <summary>Sends one notification, retrying transient FCM failures, and returns the outcome.</summary>
    /// <remarks>Propagates <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled.</remarks>
    Task<PushNotificationResponse> SendAsync(
        FcmMessageContent content,
        string token,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Sends one batch of at most 500 tokens, retrying transient FCM failures, and returns one outcome per
    /// token in the same order as <paramref name="tokens"/>. A whole-batch transport failure (after retries)
    /// is reported as a failed outcome for every token rather than thrown.
    /// </summary>
    /// <remarks>Propagates <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled.</remarks>
    Task<IReadOnlyList<PushNotificationResponse>> SendBatchAsync(
        FcmMessageContent content,
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken
    );
}
