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
    /// logs; <see langword="null"/> when no answer arrived.
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

    /// <summary>
    /// The category of the failure; <see langword="null"/> on success. See <see cref="ApnsFailureKind"/> for what
    /// each kind means and which APNs reasons map onto it.
    /// </summary>
    /// <remarks>
    /// An unknown reason — one Apple added after this classification was written — maps by its status class, so the
    /// kind is always present for a failure that carries an HTTP status.
    /// </remarks>
    public ApnsFailureKind? FailureKind { get; init; }

    /// <summary>
    /// Whether the caller may retry this send. <see langword="false"/> on success and for failures a retry of the
    /// unchanged request cannot fix: an invalid device token, a payload or configuration problem, or an
    /// authentication problem other than an expired provider token (which the provider already renewed and retried
    /// once before returning the result, so a <see cref="ApnsFailureKind.Authentication"/> result that still names
    /// <c>ExpiredProviderToken</c> stayed stale and can be tried again later).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retryable failures are <see cref="ApnsFailureKind.ServerError"/> (after 15 minutes), <see
    /// cref="ApnsFailureKind.Throttled"/> (with a delay), <see cref="ApnsFailureKind.Transport"/>, and a result
    /// whose <see cref="Reason"/> is <c>ExpiredProviderToken</c>.
    /// </para>
    /// <para>
    /// A <see cref="ApnsFailureKind.Transport"/> retry carries a duplicate risk: the fault may have happened after
    /// APNs accepted the notification, and APNs does not deduplicate, so the retry can show the notification twice.
    /// Apple's guidance for the codes it names is the authority on which failures are never worth retrying.
    /// </para>
    /// </remarks>
    public bool IsRetryable =>
        FailureKind is ApnsFailureKind.ServerError or ApnsFailureKind.Throttled or ApnsFailureKind.Transport
        // The provider's own one-shot renewal did not clear the stale token, so a later send may succeed once the
        // token can be re-minted; every other authentication result needs a configuration fix.
        || string.Equals(Reason, "ExpiredProviderToken", StringComparison.Ordinal);

    /// <summary>
    /// How long the caller should wait before retrying; <see langword="null"/> when no guidance applies.
    /// </summary>
    /// <remarks>
    /// An HTTP 5xx carries 15 minutes, because Apple says: "After 15 minutes, you can retry JSON payloads that
    /// receive response status codes that begin with 5XX." An HTTP 429 carries the <c>Retry-After</c> header's value
    /// when APNs sends one; Apple's response-header table does not list that header, so the value is
    /// <see langword="null"/> without it and the caller chooses the delay. Everything else is
    /// <see langword="null"/>.
    /// </remarks>
    public TimeSpan? RetryAfter { get; init; }
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
