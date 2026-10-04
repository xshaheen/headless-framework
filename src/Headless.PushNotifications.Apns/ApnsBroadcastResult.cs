// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns;

/// <summary>Represents the outcome of an APNs channel broadcast.</summary>
/// <remarks>
/// A broadcast dispatches to every device subscribed to the target channel.
/// APNs accepts or rejects the broadcast request in its entirety.
/// </remarks>
[PublicAPI]
public sealed record ApnsBroadcastResult
{
    /// <summary>Gets a value indicating whether APNs accepted the broadcast.</summary>
    public required bool IsSucceeded { get; init; }

    /// <summary>
    /// Gets the HTTP response status code, or <see langword="null"/> when no response was received.
    /// </summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>
    /// Gets the APNs error reason code, or <see langword="null"/> when the response succeeded or provided no reason.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the failure description, or <see langword="null"/> when delivery succeeded.
    /// </summary>
    public string? FailureError { get; init; }

    /// <summary>
    /// Gets the <c>apns-request-id</c> header value sent with the request.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the <c>apns-unique-id</c> header value returned by APNs, or <see langword="null"/> when not present.
    /// </summary>
    public string? UniqueId { get; init; }

    /// <summary>Gets the categorized failure kind, or <see langword="null"/> when delivery succeeded.</summary>
    public ApnsFailureKind? FailureKind { get; init; }

    /// <summary>
    /// Gets a value indicating whether retrying the broadcast request can succeed.
    /// </summary>
    public bool IsRetryable =>
        FailureKind is ApnsFailureKind.ServerError or ApnsFailureKind.Throttled or ApnsFailureKind.Transport;

    /// <summary>Gets the recommended retry delay, or <see langword="null"/> when none is specified.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
