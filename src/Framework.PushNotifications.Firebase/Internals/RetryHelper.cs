// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FirebaseAdmin.Messaging;

namespace Framework.PushNotifications.Firebase.Internals;

/// <summary>
/// Helper methods for FCM retry logic.
/// </summary>
internal static class RetryHelper
{
    /// <summary>
    /// Determines if a FirebaseMessagingException represents a transient error that should be retried.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>
    /// True if the error is transient (QuotaExceeded, Unavailable, Internal);
    /// false for permanent errors (Unregistered, InvalidArgument, etc.).
    /// </returns>
    /// <remarks>
    /// Error Classification Matrix:
    /// - QuotaExceeded (429): Rate limit hit → Retry with RateLimitDelay
    /// - Unavailable (503): Service temporarily down → Retry with exponential backoff
    /// - Internal (500): Server error → Retry with exponential backoff
    /// - Unregistered: Invalid device token → Don't retry (caller should remove token)
    /// - InvalidArgument: Malformed request → Don't retry (code bug)
    /// - SenderIdMismatch: Wrong credentials → Don't retry (config error)
    /// - ThirdPartyAuthError: Bad APNs cert → Don't retry (config error)
    /// </remarks>
    public static bool IsTransientError(FirebaseMessagingException exception) =>
        exception.MessagingErrorCode
            is MessagingErrorCode.QuotaExceeded
                or MessagingErrorCode.Unavailable
                or MessagingErrorCode.Internal;

    /// <summary>
    /// Extracts Retry-After delay from HTTP response headers, if present.
    /// </summary>
    /// <param name="exception">The exception containing HTTP response.</param>
    /// <param name="defaultDelay">Default delay if Retry-After header not found.</param>
    /// <returns>
    /// TimeSpan from Retry-After header, or defaultDelay if not present or invalid.
    /// </returns>
    /// <remarks>
    /// FCM returns Retry-After header with HTTP 429 QuotaExceeded.
    /// Header format: "Retry-After: 120" (seconds) or "Retry-After: Wed, 21 Oct 2015 07:28:00 GMT".
    /// </remarks>
    public static TimeSpan GetRetryAfterDelay(FirebaseMessagingException exception, TimeSpan defaultDelay)
    {
        if (exception.HttpResponse?.Headers.RetryAfter is not { } retryAfter)
            return defaultDelay;

        // Retry-After can be delta-seconds or HTTP-date
        if (retryAfter.Delta.HasValue)
            return retryAfter.Delta.Value;

        if (retryAfter.Date.HasValue)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : defaultDelay;
        }

        return defaultDelay;
    }
}
