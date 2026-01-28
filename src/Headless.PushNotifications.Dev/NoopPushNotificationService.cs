// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Abstractions;

namespace Headless.PushNotifications.Dev;

public sealed class NoopPushNotificationService : IPushNotificationService
{
    public ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default
    )
    {
        var response = PushNotificationResponse.Succeeded(clientToken, Guid.NewGuid().ToString());

        return ValueTask.FromResult(response);
    }

    public ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default
    )
    {
        var responses = clientTokens
            .Select(clientToken => PushNotificationResponse.Succeeded(clientToken, Guid.NewGuid().ToString()))
            .ToList();

        return ValueTask.FromResult(
            new BatchPushNotificationResponse
            {
                SuccessCount = clientTokens.Count,
                FailureCount = 0,
                Responses = responses,
            }
        );
    }
}
