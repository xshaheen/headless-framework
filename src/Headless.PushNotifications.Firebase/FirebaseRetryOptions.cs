// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// In-process retry of the FCM failures the FirebaseAdmin SDK does not retry itself.
/// </summary>
/// <remarks>
/// <para>
/// The SDK already retries HTTP 503 (<c>UNAVAILABLE</c>) and transport failures up to 4 times with its own
/// backoff, and that cannot be configured. This policy covers only <c>INTERNAL</c> (HTTP 500) and
/// <c>QUOTA_EXCEEDED</c> (HTTP 429); every other error returns at once.
/// </para>
/// <para>
/// Delays follow Google's guidance: <c>INTERNAL</c> waits a jittered exponential backoff that starts at 10 seconds
/// (10s, 20s, 40s, and so on, each up to 50% longer), and <c>QUOTA_EXCEEDED</c> waits the Retry-After value or 60
/// seconds, whichever is longer. A longer Retry-After is always honored. Every delay is capped at
/// <see cref="MaxDelay"/>, and a failure whose Retry-After exceeds <see cref="MaxDelay"/> is not retried. A
/// multicast resends only the tokens that failed with a retried code, one round per retry, and waits for the
/// longest delay in the round.
/// </para>
/// <para>
/// FCM has no idempotency key, so a retried message that FCM did accept can be delivered twice. Set
/// <see cref="MaxAttempts"/> to 0 when the caller retries from its own queue instead.
/// </para>
/// <para>
/// All values are validated by <see cref="FirebaseOptionsValidator"/> at application startup; the validator
/// is the single source of truth for the accepted ranges.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class FirebaseRetryOptions
{
    /// <summary>
    /// Maximum retries after the first attempt. Set to 0 to disable in-process retry.
    /// </summary>
    /// <remarks>Default: 2. Valid range: 0-5.</remarks>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>
    /// Maximum delay before any retry. Longer backoff delays are capped at this value, and a failure whose
    /// Retry-After exceeds it is not retried.
    /// </summary>
    /// <remarks>
    /// Default: 5 minutes. Valid range: 1 minute to 1 hour. The floor keeps Google's 60-second quota wait intact.
    /// </remarks>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
}
