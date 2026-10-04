// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Dev;

/// <summary>
/// Provides an in-memory no-op implementation of <see cref="IPushNotificationService"/> for development and testing.
/// Generates synthetic success responses and message identifiers without validating requests or dispatching network traffic.
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
