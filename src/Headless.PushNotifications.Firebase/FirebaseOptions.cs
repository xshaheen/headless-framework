// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Firebase configuration options.
/// </summary>
public sealed class FirebaseOptions
{
    /// <summary>
    /// Firebase service account JSON credentials.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key data. Do not log or serialize.
    /// </remarks>
    [JsonIgnore]
    public required string Json { get; init; }

    /// <summary>
    /// Retry policy configuration for FCM API calls.
    /// </summary>
    public FirebaseRetryOptions Retry { get; init; } = new();

    /// <inheritdoc />
    public override string ToString() => "FirebaseOptions { Json = [REDACTED] }";
}

/// <summary>
/// Retry policy configuration for Firebase Cloud Messaging API calls.
/// </summary>
public sealed class FirebaseRetryOptions
{
    /// <summary>
    /// Maximum retry attempts for transient failures. Set to 0 to disable retry.
    /// </summary>
    /// <remarks>
    /// Default: 5 attempts. Valid range: 0-10.
    /// Transient errors (QuotaExceeded, Unavailable, Internal) retry with exponential backoff.
    /// Permanent errors (Unregistered, InvalidArgument) return immediately.
    /// </remarks>
    public int MaxAttempts
    {
        get;
        init
        {
            field = Argument.IsInclusiveBetween(
                argument: value,
                minimumValue: 0,
                maximumValue: 10,
                argumentParamName: nameof(MaxAttempts)
            );
        }
    } = 5;

    /// <summary>
    /// Maximum delay between retry attempts. Individual retries capped at this value.
    /// </summary>
    /// <remarks>
    /// Default: 1 minute. Valid range: > 0 and &lt;= 5 minutes.
    /// Exponential backoff sequence (1s, 2s, 4s, 8s, 16s, 32s) capped at MaxDelay.
    /// </remarks>
    public TimeSpan MaxDelay
    {
        get;
        init
        {
            field = Argument.IsInclusiveBetween(
                argument: value,
                minimumValue: TimeSpan.Zero,
                maximumValue: TimeSpan.FromMinutes(5),
                argumentParamName: nameof(MaxDelay)
            );
        }
    } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Delay used when FCM returns HTTP 429 QuotaExceeded error.
    /// </summary>
    /// <remarks>
    /// Default: 60 seconds per FCM recommendation.
    /// If FCM response includes Retry-After header, that value is used instead.
    /// Valid range: > 0 and &lt;= 5 minutes.
    /// </remarks>
    public TimeSpan RateLimitDelay
    {
        get;
        init
        {
            field = Argument.IsInclusiveBetween(
                argument: value,
                minimumValue: TimeSpan.Zero,
                maximumValue: TimeSpan.FromMinutes(5),
                argumentParamName: nameof(RateLimitDelay)
            );
        }
    } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Enable jitter to prevent thundering herd on retry.
    /// </summary>
    /// <remarks>
    /// Default: true. Adds random variance (±25%) to retry delays.
    /// Distributes retry load across time to avoid overwhelming FCM servers.
    /// </remarks>
    public bool UseJitter { get; init; } = true;
}

public sealed class FirebaseOptionsValidator : AbstractValidator<FirebaseOptions>
{
    public FirebaseOptionsValidator()
    {
        RuleFor(x => x.Json).NotEmpty().WithMessage("Firebase JSON credentials must be provided.");

        RuleFor(x => x.Retry.MaxAttempts)
            .InclusiveBetween(0, 10)
            .WithMessage("Retry MaxAttempts must be between 0 and 10.");

        RuleFor(x => x.Retry.MaxDelay)
            .InclusiveBetween(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5))
            .WithMessage("Retry MaxDelay must be between 1 second and 5 minutes.");

        RuleFor(x => x.Retry.RateLimitDelay)
            .InclusiveBetween(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5))
            .WithMessage("Retry RateLimitDelay must be between 1 second and 5 minutes.");
    }
}
