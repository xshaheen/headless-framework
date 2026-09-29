// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The category of a failed APNs send, derived from the HTTP status and the <c>reason</c> APNs answered with, plus
/// <see cref="Transport"/> for a send that never got an answer.
/// </summary>
/// <remarks>
/// <para>
/// The kinds follow the retry guidance in Apple's "Handling notification responses from APNs": "After 15 minutes, you
/// can retry JSON payloads that receive response status codes that begin with 5XX. While retrying, you may employ a
/// back-off technique. Most notifications with the status code 4XX can be retried after you fix the error noted in
/// the <c>reason</c> field. Don't retry notification responses with the error code <c>BadDeviceToken</c>,
/// <c>DeviceTokenNotForTopic</c>, <c>Forbidden</c>, <c>ExpiredToken</c>, <c>Unregistered</c>, or
/// <c>PayloadTooLarge</c>. You can retry with a delay, if you get the error code <c>TooManyRequests</c>."
/// </para>
/// <para>
/// A 4XX the caller has fixed is a new request, not a retry of the failed one, which is why a fixable 4XX is
/// <see cref="Payload"/> or <see cref="Configuration"/> with <see cref="ApnsSendResult.IsRetryable"/>
/// <see langword="false"/>: the same request sent again unchanged would be rejected the same way.
/// </para>
/// </remarks>
[PublicAPI]
public enum ApnsFailureKind
{
    /// <summary>
    /// The device token is invalid, expired, unregistered, or meant for another environment or topic. Apple lists
    /// <c>BadDeviceToken</c>, <c>DeviceTokenNotForTopic</c>, <c>ExpiredToken</c>, and <c>Unregistered</c> among the
    /// codes never to retry. Remove or correct the token instead.
    /// </summary>
    DeviceTokenInvalid = 0,

    /// <summary>
    /// APNs throttled the request: <c>TooManyRequests</c> throttles one device token. Retryable, with a delay; the
    /// delay comes from the <c>Retry-After</c> header when one is present, otherwise the caller chooses.
    /// </summary>
    Throttled = 1,

    /// <summary>
    /// An APNs server error (HTTP 5xx). Retryable after 15 minutes, per Apple's guidance; the provider never retries
    /// one in-process.
    /// </summary>
    ServerError = 2,

    /// <summary>
    /// The provider token was rejected (<c>ExpiredProviderToken</c>, <c>InvalidProviderToken</c>,
    /// <c>MissingProviderToken</c>, <c>UnrelatedKeyIdInToken</c>, <c>BadEnvironmentKeyIdInToken</c>, or
    /// <c>TooManyProviderTokenUpdates</c>). Only <c>ExpiredProviderToken</c> is retryable, and the provider already
    /// re-mints and retries it once; any rejection that reaches the caller is a token or key problem to fix.
    /// </summary>
    Authentication = 3,

    /// <summary>
    /// The instance's APNs configuration is wrong: a bad or disallowed topic (<c>BadTopic</c>,
    /// <c>MissingTopic</c>, <c>TopicDisallowed</c>, <c>DeviceTokenNotForTopic</c>), a certificate problem (<c>BadCertificate</c>,
    /// <c>BadCertificateEnvironment</c>), or a forbidden action. Fix the configuration; a retry of the same request
    /// fails the same way.
    /// </summary>
    Configuration = 4,

    /// <summary>
    /// The request itself is malformed or too large: <c>PayloadTooLarge</c>, <c>PayloadEmpty</c>, and the
    /// <c>Bad*</c> and <c>Missing*</c> header errors (<c>BadCollapseId</c>, <c>BadExpirationDate</c>,
    /// <c>BadMessageId</c>, <c>BadPriority</c>, <c>InvalidPushType</c>, <c>MissingDeviceToken</c>,
    /// <c>DuplicateHeaders</c>, <c>BadPath</c>, <c>MethodNotAllowed</c>). Fix the request;
    /// Apple lists <c>PayloadTooLarge</c> among the codes never to retry.
    /// </summary>
    Payload = 5,

    /// <summary>
    /// No answer arrived: a transport fault left after the provider's connection retries, an open circuit breaker, a
    /// rate-limiter rejection, a timeout, a refused endpoint, or APNs' <c>IdleTimeout</c>. Retryable, with one
    /// caveat: a connection lost after the request was sent may have delivered the notification, and APNs does not
    /// deduplicate, so a retry can show it twice. See <see cref="ApnsSendResult.IsRetryable"/> remarks.
    /// </summary>
    Transport = 6,
}
