// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// The outcome of one FCM send to one target (a token, a topic, or a condition): the provider-agnostic
/// <see cref="Response"/> plus the FCM details of a failure.
/// </summary>
[PublicAPI]
public sealed record FcmSendResult
{
    /// <summary>
    /// The provider-agnostic outcome, the same value the shared <see cref="IPushNotificationService"/> returns. Its
    /// <see cref="PushNotificationResponse.ClientIdentifier"/> is the token, topic, or condition the send addressed,
    /// and its <see cref="PushNotificationResponse.MessageId"/> is FCM's message name on success.
    /// </summary>
    public required PushNotificationResponse Response { get; init; }

    /// <summary>
    /// The error code FCM answered with, as it appears on the wire: the FCM code such as <c>UNREGISTERED</c> or
    /// <c>QUOTA_EXCEEDED</c>, or the platform code such as <c>NOT_FOUND</c> when FCM sent none;
    /// <see langword="null"/> on success or when no answer arrived.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// The category of the failure; <see langword="null"/> on success. See <see cref="FcmFailureKind"/> for which
    /// codes map onto each kind. An unregistered target carries <see cref="FcmFailureKind.TokenInvalid"/>.
    /// </summary>
    public FcmFailureKind? FailureKind { get; init; }

    /// <summary>
    /// Whether the caller may retry this send later: <see langword="true"/> only for
    /// <see cref="FcmFailureKind.Throttled"/>, <see cref="FcmFailureKind.ServerError"/>, and
    /// <see cref="FcmFailureKind.Transport"/>.
    /// </summary>
    /// <remarks>
    /// The provider has already spent its own retries (<see cref="FirebaseRetryOptions"/>) before returning the
    /// result. FCM has no idempotency key, so a retry of a send FCM did accept delivers it twice.
    /// </remarks>
    public bool IsRetryable =>
        FailureKind is FcmFailureKind.Throttled or FcmFailureKind.ServerError or FcmFailureKind.Transport;

    /// <summary>
    /// How long to wait before retrying; <see langword="null"/> when the failure is not retryable or is a transport
    /// failure.
    /// </summary>
    /// <remarks>
    /// Throttling carries FCM's Retry-After when it sent one, otherwise 60 seconds, as Google advises for
    /// <c>QUOTA_EXCEEDED</c>. A server error carries Retry-After when present, otherwise 10 seconds, Google's minimum
    /// wait before retrying a failed request; back off exponentially from there.
    /// </remarks>
    public TimeSpan? RetryAfter { get; init; }
}

/// <summary>The outcome of an FCM multicast: one <see cref="FcmSendResult"/> per token, in input order.</summary>
[PublicAPI]
public sealed record FcmBatchSendResult
{
    /// <summary>The number of tokens FCM accepted the message for.</summary>
    public required int SuccessCount { get; init; }

    /// <summary>The number of tokens that failed or were reported as unregistered.</summary>
    public required int FailureCount { get; init; }

    /// <summary>One result per token, in the order the tokens were given.</summary>
    public required IReadOnlyList<FcmSendResult> Results { get; init; }
}
