// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// The category of a failed FCM send, derived from the FCM error code, the platform error code FCM answered with
/// when there is no FCM code, or the exception raised when no answer arrived.
/// </summary>
/// <remarks>
/// The kinds follow Google's FCM error-code guidance: do not retry 400, 401, 403, or 404; retry 500 and 503 with
/// exponential backoff; retry 429 after its Retry-After. A 4xx the caller has fixed is a new request, not a retry,
/// which is why <see cref="Payload"/>, <see cref="Configuration"/>, and <see cref="Authentication"/> are not
/// retryable: the same request sent again unchanged fails the same way.
/// </remarks>
[PublicAPI]
public enum FcmFailureKind
{
    /// <summary>
    /// The target is no longer valid: FCM's <c>UNREGISTERED</c>, and <c>SENDER_ID_MISMATCH</c> when
    /// <see cref="FirebaseOptions.TreatSenderIdMismatchAsUnregistered"/> is set. The result's status is
    /// <see cref="PushNotificationResponseStatus.Unregistered"/>; delete the token.
    /// </summary>
    TokenInvalid = 0,

    /// <summary>
    /// FCM throttled the sender: <c>QUOTA_EXCEEDED</c>, or a platform <c>RESOURCE_EXHAUSTED</c>. Retryable after
    /// <see cref="FcmSendResult.RetryAfter"/>.
    /// </summary>
    Throttled = 1,

    /// <summary>
    /// An FCM server error: <c>INTERNAL</c>, <c>UNAVAILABLE</c>, or another HTTP 5xx. Retryable with exponential
    /// backoff, starting at <see cref="FcmSendResult.RetryAfter"/>.
    /// </summary>
    ServerError = 2,

    /// <summary>
    /// A credential was rejected: <c>THIRD_PARTY_AUTH_ERROR</c> (the APNs certificate or key, or the web push
    /// credentials, uploaded to the Firebase project), a platform <c>UNAUTHENTICATED</c>, or the service account
    /// credentials could not be loaded or exchanged for an access token. Fix the credentials.
    /// </summary>
    Authentication = 3,

    /// <summary>
    /// The Firebase project setup is wrong: <c>SENDER_ID_MISMATCH</c> (the token belongs to another project; see
    /// <see cref="FirebaseOptions.TreatSenderIdMismatchAsUnregistered"/>), or a platform <c>PERMISSION_DENIED</c> or
    /// <c>NOT_FOUND</c> without an FCM code, such as a project without the FCM API enabled.
    /// </summary>
    Configuration = 4,

    /// <summary>
    /// FCM rejected the message itself: <c>INVALID_ARGUMENT</c>, including a payload over the size limit or a
    /// malformed token. The message is never read as a dead token: Google advises deleting a token on
    /// <c>INVALID_ARGUMENT</c> only when the error is about the token, which the code alone cannot tell.
    /// </summary>
    Payload = 5,

    /// <summary>
    /// No answer arrived: a network failure or timeout left after the SDK's own retries. Retryable, with one caveat:
    /// FCM has no idempotency key, so a request lost after FCM accepted it is delivered twice when retried.
    /// </summary>
    Transport = 6,
}
