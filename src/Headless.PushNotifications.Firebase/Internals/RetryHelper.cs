// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FirebaseAdmin.Messaging;

namespace Headless.PushNotifications.Firebase.Internals;

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
    public static bool IsTransientError(FirebaseMessagingException exception)
    {
        return exception.MessagingErrorCode
            is MessagingErrorCode.QuotaExceeded
                or MessagingErrorCode.Unavailable
                or MessagingErrorCode.Internal;
    }

    /// <summary>
    /// Extracts the Retry-After delay from HTTP response headers (if present), clamped to <paramref name="maxDelay"/>.
    /// </summary>
    /// <param name="exception">The exception containing the HTTP response.</param>
    /// <param name="defaultDelay">Default delay when no usable Retry-After header is present.</param>
    /// <param name="maxDelay">Upper bound applied to the returned delay. Polly does not cap delays produced by a delay generator, so the cap is applied here.</param>
    /// <param name="timeProvider">Clock used to compute the delay for HTTP-date Retry-After values.</param>
    /// <returns>
    /// The Retry-After delay, or <paramref name="defaultDelay"/> when not present or invalid, never exceeding
    /// <paramref name="maxDelay"/>.
    /// </returns>
    /// <remarks>
    /// FCM returns a Retry-After header with HTTP 429 QuotaExceeded.
    /// Header format: "Retry-After: 120" (seconds) or "Retry-After: Wed, 21 Oct 2015 07:28:00 GMT".
    /// </remarks>
    public static TimeSpan GetRetryAfterDelay(
        FirebaseMessagingException exception,
        TimeSpan defaultDelay,
        TimeSpan maxDelay,
        TimeProvider timeProvider
    )
    {
        var delay = _ResolveRetryAfterDelay(exception, defaultDelay, timeProvider);

        return delay > maxDelay ? maxDelay : delay;
    }

    private static TimeSpan _ResolveRetryAfterDelay(
        FirebaseMessagingException exception,
        TimeSpan defaultDelay,
        TimeProvider timeProvider
    )
    {
        if (exception.HttpResponse?.Headers.RetryAfter is not { } retryAfter)
        {
            return defaultDelay;
        }

        // Retry-After can be delta-seconds or HTTP-date.
        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter.Date.HasValue)
        {
            var delay = retryAfter.Date.Value - timeProvider.GetUtcNow();
            return delay > TimeSpan.Zero ? delay : defaultDelay;
        }

        return defaultDelay;
    }
}
