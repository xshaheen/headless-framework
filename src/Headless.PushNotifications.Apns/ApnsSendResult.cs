// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The outcome of one APNs send to one device token: the provider-agnostic <see cref="Response"/> plus the details
/// APNs returned with it.
/// </summary>
[PublicAPI]
public sealed record ApnsSendResult
{
    /// <summary>The provider-agnostic outcome, the same value the shared <see cref="IPushNotificationService"/> returns.</summary>
    public required PushNotificationResponse Response { get; init; }

    /// <summary>
    /// The HTTP status APNs answered with, or <see langword="null"/> when no answer arrived: a transport fault, a
    /// timeout, an open circuit, or an endpoint the provider refused to call.
    /// </summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>
    /// The APNs error code from the response body, such as <c>BadDeviceToken</c> or <c>Unregistered</c>;
    /// <see langword="null"/> on success or when the body carried none.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The <c>apns-id</c> the request carried, which APNs echoes and which identifies the notification in Apple's
    /// logs. When no answer arrived it is the caller's <see cref="ApnsNotification.ApnsId"/> if one was set, and
    /// <see langword="null"/> otherwise.
    /// </summary>
    public string? ApnsId { get; init; }

    /// <summary>
    /// The <c>apns-unique-id</c> response header, which only the sandbox environment returns and which looks the
    /// notification up in Apple's delivery log; <see langword="null"/> when APNs sent none.
    /// </summary>
    public string? UniqueId { get; init; }

    /// <summary>
    /// For an HTTP 410, the instant APNs confirmed the device token was no longer valid for the topic;
    /// <see langword="null"/> for any other status or when the body carried no timestamp.
    /// </summary>
    /// <remarks>
    /// A token the app registered after this instant is still valid, so compare it with the token's registration
    /// time before deleting the token.
    /// </remarks>
    public DateTimeOffset? InvalidSince { get; init; }
}

/// <summary>The outcome of an APNs multicast: one <see cref="ApnsSendResult"/> per device token, in input order.</summary>
[PublicAPI]
public sealed record ApnsBatchSendResult
{
    /// <summary>The number of device tokens APNs accepted the notification for.</summary>
    public required int SuccessCount { get; init; }

    /// <summary>The number of device tokens that failed or were reported as unregistered.</summary>
    public required int FailureCount { get; init; }

    /// <summary>One result per device token, in the order the tokens were given.</summary>
    public required IReadOnlyList<ApnsSendResult> Results { get; init; }
}
