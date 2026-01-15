// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.PushNotifications.Firebase;

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
    public RetryOptions Retry { get; init; } = new();

    /// <inheritdoc />
    public override string ToString() => "FirebaseOptions { Json = [REDACTED] }";
}

/// <summary>
/// Retry policy configuration for Firebase Cloud Messaging API calls.
/// </summary>
public sealed class RetryOptions
{
    private int _maxAttempts = 5;
    private TimeSpan _maxDelay = TimeSpan.FromMinutes(1);
    private TimeSpan _rateLimitDelay = TimeSpan.FromSeconds(60);

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
        get => _maxAttempts;
        init =>
            _maxAttempts = value is >= 0 and <= 10
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "MaxAttempts must be 0-10");
    }

    /// <summary>
    /// Maximum delay between retry attempts. Individual retries capped at this value.
    /// </summary>
    /// <remarks>
    /// Default: 1 minute. Valid range: > 0 and &lt;= 5 minutes.
    /// Exponential backoff sequence (1s, 2s, 4s, 8s, 16s, 32s) capped at MaxDelay.
    /// </remarks>
    public TimeSpan MaxDelay
    {
        get => _maxDelay;
        init =>
            _maxDelay =
                value > TimeSpan.Zero && value <= TimeSpan.FromMinutes(5)
                    ? value
                    : throw new ArgumentOutOfRangeException(nameof(value), "MaxDelay must be > 0 and <= 5 minutes");
    }

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
        get => _rateLimitDelay;
        init =>
            _rateLimitDelay =
                value > TimeSpan.Zero && value <= TimeSpan.FromMinutes(5)
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(value),
                        "RateLimitDelay must be > 0 and <= 5 minutes"
                    );
    }

    /// <summary>
    /// Enable jitter to prevent thundering herd on retry.
    /// </summary>
    /// <remarks>
    /// Default: true. Adds random variance (±25%) to retry delays.
    /// Distributes retry load across time to avoid overwhelming FCM servers.
    /// </remarks>
    public bool UseJitter { get; init; } = true;
}
