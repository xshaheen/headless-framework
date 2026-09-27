// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Checks;
using Headless.PushNotifications.Firebase.Internals;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Firebase Cloud Messaging (FCM) push notification service. Validates input, splits multicast sends into
/// FCM-sized batches, and aggregates per-FID outcomes. All FCM interaction and transient-failure retry is
/// delegated to <see cref="IFcmMessageSender"/>.
/// </summary>
internal sealed class FcmPushNotificationService(IFcmMessageSender sender) : IPushNotificationService
{
    private const int _MaxTitleLength = 100;
    private const int _MaxBodyLength = 4000;
    private const int _MaxFidsPerBatch = 500;

    // Forwarded to APNs as apns-collapse-id through the iOS bridge, and Apple caps that header at 64 UTF-8 bytes.
    private const int _MaxCollapseKeyBytes = 64;

    public async ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientIdentifier,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(clientIdentifier);
        Argument.IsNotNull(request);
        _ValidateContent(request);

        var content = FcmMessageContent.From(request);

        return await sender.SendAsync(content, clientIdentifier, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(clientIdentifiers);
        Argument.IsNotNull(request);
        _ValidateContent(request);

        var content = FcmMessageContent.From(request);
        var responses = new List<PushNotificationResponse>(clientIdentifiers.Count);
        var successCount = 0;
        var failureCount = 0;

        foreach (var batch in clientIdentifiers.Chunk(_MaxFidsPerBatch))
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
        PushNotificationRequestValidation.Validate(request);

        // Both are set together or not at all once the shared rules pass.
        if (request is { Title: { } title, Body: { } body })
        {
            Argument.IsLessThanOrEqualTo(title.Length, _MaxTitleLength);
            Argument.IsLessThanOrEqualTo(body.Length, _MaxBodyLength);
        }

        _EnsureDataAllowed(request.Data);

        if (request.CollapseKey is not null)
        {
            Argument.IsLessThanOrEqualTo(Encoding.UTF8.GetByteCount(request.CollapseKey), _MaxCollapseKeyBytes);
        }
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
