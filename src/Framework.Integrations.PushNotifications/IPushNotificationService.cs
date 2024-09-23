// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Integrations.PushNotifications;

using JetBrainsPure = PureAttribute;
using SystemPure = System.Diagnostics.Contracts.PureAttribute;

public interface IPushNotificationService
{
    [SystemPure]
    [JetBrainsPure]
    ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null
    );

    [SystemPure]
    [JetBrainsPure]
    ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null
    );
}
