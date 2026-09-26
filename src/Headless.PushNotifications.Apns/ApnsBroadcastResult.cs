// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns;

/// <summary>The outcome of one APNs broadcast to a channel.</summary>
/// <remarks>
/// A broadcast reaches every device subscribed to the channel through one request, so there is no per-device result:
/// APNs accepts or rejects the broadcast as a whole.
/// </remarks>
[PublicAPI]
public sealed record ApnsBroadcastResult
{
    /// <summary>Whether APNs accepted the broadcast (HTTP 200).</summary>
    public required bool IsSucceeded { get; init; }

    /// <summary>
    /// The HTTP status APNs answered with, or <see langword="null"/> when no answer arrived: a transport fault, a
    /// timeout, an open circuit, or an endpoint the provider refused to call.
    /// </summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>
    /// The APNs error code from the response body, such as <c>BadChannelId</c>; <see langword="null"/> on success or
    /// when the body carried none.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// A description of the failure: the reason and status for a rejection, or <c>"&lt;ExceptionType&gt;: &lt;message&gt;"</c>
    /// when no answer arrived; <see langword="null"/> on success.
    /// </summary>
    public string? FailureError { get; init; }

    /// <summary>
    /// The <c>apns-request-id</c> the request carried, which APNs echoes and which identifies the broadcast when it
    /// reports an error. The caller's <see cref="ApnsNotification.ApnsId"/> when set, otherwise a generated UUID.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// The <c>apns-unique-id</c> response header, a server-generated identifier to quote when raising a
    /// troubleshooting request with Apple; <see langword="null"/> when APNs sent none.
    /// </summary>
    public string? UniqueId { get; init; }

    /// <summary>The kind of failure, or <see langword="null"/> on success.</summary>
    public ApnsFailureKind? FailureKind { get; init; }

    /// <summary>
    /// Whether sending the same broadcast again later can succeed. See <see cref="ApnsSendResult.IsRetryable"/> for
    /// the rules; a broadcast has no device token, so <see cref="ApnsFailureKind.DeviceTokenInvalid"/> never applies.
    /// </summary>
    public bool IsRetryable =>
        FailureKind is ApnsFailureKind.ServerError or ApnsFailureKind.Throttled or ApnsFailureKind.Transport;

    /// <summary>How long to wait before retrying, when APNs or Apple's guidance says; otherwise <see langword="null"/>.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
