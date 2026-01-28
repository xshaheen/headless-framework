// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;

namespace Headless.PushNotifications.Abstractions;

public interface IPushNotificationService
{
    ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default
    );

    ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default
    );
}
