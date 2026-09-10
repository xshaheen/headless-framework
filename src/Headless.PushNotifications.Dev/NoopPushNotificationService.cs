// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Dev;

/// <summary>
/// No-op <see cref="IPushNotificationService"/> for local development and testing. It sends nothing and
/// always reports success, returning a freshly generated GUID as the message id for every client identifier. It never
/// validates input or throws (so it stays inert for any caller); do not use in production.
/// </summary>
internal sealed class NoopPushNotificationService : IPushNotificationService
{
    public ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientIdentifier,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var response = PushNotificationResponse.SucceededUnchecked(clientIdentifier, Guid.NewGuid().ToString());

        return ValueTask.FromResult(response);
    }

    public ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var responses = new List<PushNotificationResponse>(clientIdentifiers.Count);
        foreach (var clientIdentifier in clientIdentifiers)
        {
            responses.Add(PushNotificationResponse.SucceededUnchecked(clientIdentifier, Guid.NewGuid().ToString()));
        }

        return ValueTask.FromResult(
            new BatchPushNotificationResponse
            {
                SuccessCount = clientIdentifiers.Count,
                FailureCount = 0,
                Responses = responses,
            }
        );
    }
}
