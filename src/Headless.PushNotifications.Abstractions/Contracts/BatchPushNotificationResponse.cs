// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

public sealed class BatchPushNotificationResponse
{
    public required int SuccessCount { get; init; }

    public required int FailureCount { get; init; }

    public required IReadOnlyList<PushNotificationResponse> Responses { get; init; }
}
