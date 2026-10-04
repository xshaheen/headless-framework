// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Represents the outcome of an APNs send operation for a single device token.
/// </summary>
[PublicAPI]
public sealed record ApnsSendResult
{
    /// <summary>Gets the provider-neutral outcome response.</summary>
    public required PushNotificationResponse Response { get; init; }

    /// <summary>
    /// Gets the HTTP response status code, or <see langword="null"/> when no response was received.
    /// </summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>
    /// Gets the APNs error reason code, or <see langword="null"/> when the send succeeded or provided no reason.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the <c>apns-id</c> header value for the notification.
    /// </summary>
    public string? ApnsId { get; init; }

    /// <summary>
    /// Gets the <c>apns-unique-id</c> response header value returned by sandbox APNs, or <see langword="null"/> when absent.
    /// </summary>
    public string? UniqueId { get; init; }

    /// <summary>
    /// Gets the timestamp when APNs determined the device token became invalid, or <see langword="null"/> when not applicable.
    /// </summary>
    public DateTimeOffset? InvalidSince { get; init; }

    /// <summary>
    /// Gets the categorized failure kind, or <see langword="null"/> when delivery succeeded.
    /// </summary>
    public ApnsFailureKind? FailureKind { get; init; }

    /// <summary>
    /// Gets a value indicating whether retrying the send operation can succeed.
    /// </summary>
    public bool IsRetryable =>
        FailureKind is ApnsFailureKind.ServerError or ApnsFailureKind.Throttled or ApnsFailureKind.Transport
        || string.Equals(Reason, "ExpiredProviderToken", StringComparison.Ordinal);

    /// <summary>
    /// Gets the recommended retry delay, or <see langword="null"/> when none is specified.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }
}

/// <summary>Represents the outcome of an APNs multicast send operation.</summary>
[PublicAPI]
public sealed record ApnsBatchSendResult
{
    /// <summary>Gets the number of device tokens accepted by APNs.</summary>
    public required int SuccessCount { get; init; }

    /// <summary>Gets the number of device tokens that failed delivery or are unregistered.</summary>
    public required int FailureCount { get; init; }

    /// <summary>Gets the individual result for each requested device token.</summary>
    public required IReadOnlyList<ApnsSendResult> Results { get; init; }
}
