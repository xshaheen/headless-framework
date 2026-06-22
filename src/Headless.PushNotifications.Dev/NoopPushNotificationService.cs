// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications.Abstractions;

namespace Headless.PushNotifications.Dev;

/// <summary>
/// No-op <see cref="IPushNotificationService"/> for local development and testing. It sends nothing and
/// always reports success, returning a freshly generated GUID as the message id for every token. It never
/// validates input or throws (so it stays inert for any caller); do not use in production.
/// </summary>
internal sealed class NoopPushNotificationService : IPushNotificationService
{
    public ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default
    )
    {
        var response = PushNotificationResponse.SucceededUnchecked(clientToken, Guid.NewGuid().ToString());

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
        var responses = new List<PushNotificationResponse>(clientTokens.Count);
        foreach (var clientToken in clientTokens)
        {
            responses.Add(PushNotificationResponse.SucceededUnchecked(clientToken, Guid.NewGuid().ToString()));
        }

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
