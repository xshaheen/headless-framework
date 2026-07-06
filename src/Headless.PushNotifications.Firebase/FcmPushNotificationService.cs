// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.PushNotifications.Firebase.Internals;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Firebase Cloud Messaging (FCM) push notification service. Validates input, splits multicast sends into
/// FCM-sized batches, and aggregates per-token outcomes. All FCM interaction and transient-failure retry is
/// delegated to <see cref="IFcmMessageSender"/>.
/// </summary>
internal sealed class FcmPushNotificationService(IFcmMessageSender sender) : IPushNotificationService
{
    private const int _MaxTitleLength = 100;
    private const int _MaxBodyLength = 4000;
    private const int _MaxTokensPerBatch = 500;

    public async ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(clientToken);
        Argument.IsNotNull(request);
        _ValidateContent(request);

        var content = new FcmMessageContent(request.Title, request.Body, request.Data);

        return await sender.SendAsync(content, clientToken, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientTokens,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(clientTokens);
        Argument.IsNotNull(request);
        _ValidateContent(request);

        var content = new FcmMessageContent(request.Title, request.Body, request.Data);
        var responses = new List<PushNotificationResponse>(clientTokens.Count);
        var successCount = 0;
        var failureCount = 0;

        foreach (var batch in clientTokens.Chunk(_MaxTokensPerBatch))
        {
            var batchResponses = await sender.SendBatchAsync(content, batch, cancellationToken).ConfigureAwait(false);

            foreach (var response in batchResponses)
            {
                responses.Add(response);

                if (response.IsSucceeded())
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }
        }

        return new BatchPushNotificationResponse
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            Responses = responses,
        };
    }

    private static void _ValidateContent(PushNotificationRequest request)
    {
        Argument.IsNotNullOrWhiteSpace(request.Title);
        Argument.IsNotNullOrWhiteSpace(request.Body);
        Argument.IsLessThanOrEqualTo(request.Title.Length, _MaxTitleLength);
        Argument.IsLessThanOrEqualTo(request.Body.Length, _MaxBodyLength);
        _EnsureDataAllowed(request.Data);
    }

    private static void _EnsureDataAllowed(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null)
        {
            return;
        }

        foreach (var key in data.Keys)
        {
            if (
                key is "from" or "notification" or "message_type"
                || key.StartsWith("google", StringComparison.Ordinal)
                || key.StartsWith("gcm", StringComparison.Ordinal)
            )
            {
                throw new ArgumentException($"Notification data contains the reserved FCM key '{key}'.", nameof(data));
            }
        }
    }
}
