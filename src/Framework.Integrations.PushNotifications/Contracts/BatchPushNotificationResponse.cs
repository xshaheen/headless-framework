// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Integrations.PushNotifications;

public sealed class BatchPushNotificationResponse
{
    public int SuccessCount { get; init; }

    public int FailureCount { get; init; }

    public IReadOnlyList<PushNotificationResponse> Responses { get; init; } = [];
}
