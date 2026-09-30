// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Firebase configuration options.
/// </summary>
[PublicAPI]
public sealed class FirebaseOptions
{
    /// <summary>
    /// Firebase service account JSON credentials.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key data. Do not log or serialize.
    /// </remarks>
    [JsonIgnore]
    public required string Json { get; set; }

    /// <summary>
    /// Retry policy configuration for FCM API calls.
    /// </summary>
    public FirebaseRetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Whether FCM's <c>SENDER_ID_MISMATCH</c> error is reported as
    /// <see cref="PushNotificationResponseStatus.Unregistered"/>, which tells the caller to delete the token.
    /// </summary>
    /// <remarks>
    /// Default: <see langword="false"/>, so the error is reported as
    /// <see cref="PushNotificationResponseStatus.Failure"/>. FCM returns it when the token belongs to a different
    /// Firebase project than the credentials. That is a dead token only when the credentials are right; a host
    /// configured with the wrong project's credentials gets it for every token, and treating it as unregistered
    /// would delete every valid token. Enable it once the credentials are known to be correct.
    /// </remarks>
    public bool TreatSenderIdMismatchAsUnregistered { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "FirebaseOptions { Json = [REDACTED] }";
    }
}

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

/// <summary>
/// FluentValidation validator for <see cref="FirebaseOptions"/>. Wired up and executed automatically by the
/// <c>UseFirebase</c> setup methods.
/// </summary>
internal sealed class FirebaseOptionsValidator : AbstractValidator<FirebaseOptions>
{
    public FirebaseOptionsValidator()
    {
        RuleFor(x => x.Json).NotEmpty().WithMessage("Firebase JSON credentials must be provided.");

        RuleFor(x => x.Retry.MaxAttempts)
            .InclusiveBetween(0, 5)
            .WithMessage("Retry MaxAttempts must be between 0 and 5.");

        RuleFor(x => x.Retry.MaxDelay)
            .InclusiveBetween(TimeSpan.FromMinutes(1), TimeSpan.FromHours(1))
            .WithMessage("Retry MaxDelay must be between 1 minute and 1 hour.");
    }
}
