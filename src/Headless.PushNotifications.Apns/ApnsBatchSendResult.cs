// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns;

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
