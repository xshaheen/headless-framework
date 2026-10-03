// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FirebaseAdmin;
using FirebaseAdmin.Messaging;

namespace Headless.PushNotifications.Firebase.Internal;

/// <summary>Decides whether and how long to wait before resending an FCM message, following Google's guidance.</summary>
internal static class RetryHelper
{
    /// <summary>Google: wait at least 10 seconds before retrying a failed request.</summary>
    internal static readonly TimeSpan MinServerErrorDelay = TimeSpan.FromSeconds(10);

    /// <summary>Google: without a Retry-After header, wait 60 seconds after <c>QUOTA_EXCEEDED</c>.</summary>
    internal static readonly TimeSpan MinQuotaDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Returns how long to wait before retry number <paramref name="retry"/> + 1, or <see langword="null"/> when the
    /// failure must not be retried.
    /// </summary>
    /// <remarks>
    /// Only <c>INTERNAL</c> (500) and <c>QUOTA_EXCEEDED</c> (429) are retried here. The SDK already retries 503 and
    /// transport exceptions itself, so retrying those again would multiply its attempts; every other code is
    /// permanent. <c>INTERNAL</c> waits a jittered exponential backoff of 10s, 20s, 40s, and so on, each up to 50%
    /// longer. <c>QUOTA_EXCEEDED</c> waits the longer of Retry-After and 60 seconds. Both honor a longer Retry-After
    /// and are capped at <paramref name="maxDelay"/>; when Retry-After itself exceeds <paramref name="maxDelay"/>,
    /// the failure is not retried, because retrying before the server allows only spends the quota again.
    /// </remarks>
    public static TimeSpan? GetRetryDelay(Exception? failure, int retry, TimeSpan maxDelay, TimeProvider timeProvider)
    {
        if (failure is not FirebaseMessagingException { MessagingErrorCode: { } code } exception)
        {
            return null;
        }

        TimeSpan floor;

        switch (code)
        {
            case MessagingErrorCode.Internal:
                var backoff = MinServerErrorDelay * Math.Pow(2, retry);
#pragma warning disable CA5394 // False positive: non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
                floor = backoff + (backoff * (Random.Shared.NextDouble() / 2));
#pragma warning restore CA5394
                break;
            case MessagingErrorCode.QuotaExceeded:
                floor = MinQuotaDelay;
                break;
            default:
                return null;
        }

        var retryAfter = GetRetryAfter(exception, timeProvider);

        if (retryAfter > maxDelay)
        {
            return null;
        }

        var delay = retryAfter > floor ? retryAfter.Value : floor;

        return delay > maxDelay ? maxDelay : delay;
    }

    /// <summary>
    /// Reads the Retry-After header of the FCM response, as delta-seconds or an HTTP date, or returns
    /// <see langword="null"/> when there is none or it has already passed.
    /// </summary>
    internal static TimeSpan? GetRetryAfter(FirebaseException exception, TimeProvider timeProvider)
    {
        var retryAfter = exception.HttpResponse?.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - timeProvider.GetUtcNow();

            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }
}
