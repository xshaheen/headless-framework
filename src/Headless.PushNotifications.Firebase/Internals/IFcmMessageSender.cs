// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>
/// Seam over Firebase Cloud Messaging. Owns all <c>FirebaseAdmin</c> interaction (app lifecycle, the send itself,
/// transient-error retry, outcome classification, and telemetry) and returns <see cref="FcmSendResult"/> values,
/// keeping <see cref="FcmPushNotificationService"/> testable. Messages arrive already validated.
/// </summary>
internal interface IFcmMessageSender
{
    /// <summary>
    /// Sends one message to one target, retrying transient FCM failures, and returns the outcome. Every failure,
    /// including a timeout or a credential error, is returned as a failed outcome rather than thrown.
    /// </summary>
    /// <remarks>Throws <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/> is cancelled.</remarks>
    Task<FcmSendResult> SendAsync(FcmMessage message, FcmTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Sends one batch of at most 500 FIDs, resending only the FIDs that failed transiently, and returns one
    /// outcome per FID in the same order as <paramref name="fids"/>. A whole-batch failure is reported as a
    /// failed outcome for every FID rather than thrown.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled, even
    /// though the SDK itself reports a cancelled batch as per-message failures.
    /// </remarks>
    Task<IReadOnlyList<FcmSendResult>> SendBatchAsync(
        FcmMessage message,
        IReadOnlyList<string> fids,
        CancellationToken cancellationToken
    );
}
