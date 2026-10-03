// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

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
