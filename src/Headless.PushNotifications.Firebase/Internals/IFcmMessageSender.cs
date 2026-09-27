// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>
/// The content sent to one or more Firebase Installation IDs (FIDs). A <see langword="null"/>
/// <see cref="Title"/> and <see cref="Body"/> make it a data-only message.
/// </summary>
internal sealed record FcmMessageContent(
    string? Title,
    string? Body,
    IReadOnlyDictionary<string, string>? Data,
    string? CollapseKey = null,
    int? Badge = null,
    string? Sound = null,
    PushNotificationPriority? Priority = null,
    TimeSpan? TimeToLive = null
)
{
    public bool IsDataOnly => Title is null && Body is null;

    public static FcmMessageContent From(PushNotificationRequest request)
    {
        return new FcmMessageContent(
            request.Title,
            request.Body,
            request.Data,
            request.CollapseKey,
            request.Badge,
            request.Sound,
            request.Priority,
            request.TimeToLive
        );
    }
}

/// <summary>
/// Seam over Firebase Cloud Messaging. Owns all <c>FirebaseAdmin</c> interaction (app lifecycle, message
/// construction, transient-error retry, and outcome classification) and returns provider-agnostic
/// <see cref="PushNotificationResponse"/> values, keeping <see cref="FcmPushNotificationService"/> testable.
/// </summary>
internal interface IFcmMessageSender
{
    /// <summary>
    /// Sends one notification, retrying transient FCM failures, and returns the outcome. Every failure, including
    /// a timeout or a credential error, is returned as a failed outcome rather than thrown.
    /// </summary>
    /// <remarks>Throws <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/> is cancelled.</remarks>
    Task<PushNotificationResponse> SendAsync(
        FcmMessageContent content,
        string fid,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Sends one batch of at most 500 FIDs, resending only the FIDs that failed transiently, and returns one
    /// outcome per FID in the same order as <paramref name="fids"/>. A whole-batch failure is reported as a
    /// failed outcome for every FID rather than thrown.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled, even
    /// though the SDK itself reports a cancelled multicast as per-message failures.
    /// </remarks>
    Task<IReadOnlyList<PushNotificationResponse>> SendBatchAsync(
        FcmMessageContent content,
        IReadOnlyList<string> fids,
        CancellationToken cancellationToken
    );
}
