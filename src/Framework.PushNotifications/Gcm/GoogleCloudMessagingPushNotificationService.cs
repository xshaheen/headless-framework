// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using FirebaseAdmin.Messaging;
using Framework.Checks;
using Framework.PushNotifications.Internals;
using Microsoft.Extensions.Logging;

namespace Framework.PushNotifications.Gcm;

public sealed class GoogleCloudMessagingPushNotificationService(
    ILogger<GoogleCloudMessagingPushNotificationService> logger
) : IPushNotificationService
{
    public async ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null
    )
    {
        Argument.IsNotNullOrWhiteSpace(clientToken);
        Argument.IsNotNullOrWhiteSpace(title);
        Argument.IsNotNullOrWhiteSpace(body);

        if (
            data is not null
            && (data.ContainsKey("from") || data.ContainsKey("notification") || data.ContainsKey("message_type"))
        )
        {
            throw new InvalidOperationException("Notification data contain reserved word(s).");
        }

        var message = new Message
        {
            Token = clientToken,
            Data = data,
            Notification = new Notification { Title = title, Body = body },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig { Aps = new Aps { Badge = 1 } },
        };

        try
        {
            var messageId = await FirebaseMessaging.DefaultInstance.SendAsync(message);

            return PushNotificationResponse.Succeeded(clientToken, messageId);
        }
        catch (FirebaseMessagingException e) when (e.MessagingErrorCode is MessagingErrorCode.Unregistered)
        {
            return PushNotificationResponse.Unregistered(clientToken);
        }
        catch (FirebaseMessagingException e)
        {
            return PushNotificationResponse.Failed(clientToken, _FirebaseMessagingExceptionToString(e));
        }
        catch (Exception e)
        {
            logger.FailedToSendPushNotification(e, clientToken);

            return PushNotificationResponse.Failed(clientToken, e.ToString());
        }
    }

    public async ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null
    )
    {
        Argument.IsNotNullOrEmpty(clientTokens);
        Argument.IsNotNullOrWhiteSpace(title);
        Argument.IsNotNullOrWhiteSpace(body);

        if (
            data is not null
            && (data.ContainsKey("from") || data.ContainsKey("notification") || data.ContainsKey("message_type"))
        )
        {
            throw new InvalidOperationException("Notification data contain reserved word(s).");
        }

        var message = new MulticastMessage
        {
            Tokens = clientTokens,
            Notification = new Notification { Title = title, Body = body },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig { Aps = new Aps { Badge = 1 } },
        };

        var batchResponse = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
        Debug.Assert(
            batchResponse.Responses.Count == clientTokens.Count,
            "batchResponse.Responses.Count == clientToken.Count"
        );

        return new BatchPushNotificationResponse
        {
            SuccessCount = batchResponse.SuccessCount,
            FailureCount = batchResponse.FailureCount,
            Responses = batchResponse
                .Responses.Zip(clientTokens)
                .Select(args =>
                {
                    var (response, token) = args;

                    if (response.IsSuccess)
                    {
                        return PushNotificationResponse.Succeeded(token, response.MessageId);
                    }

                    if (response.Exception.MessagingErrorCode == MessagingErrorCode.Unregistered)
                    {
                        return PushNotificationResponse.Unregistered(token);
                    }

                    return PushNotificationResponse.Failed(
                        token,
                        _FirebaseMessagingExceptionToString(response.Exception)
                    );
                })
                .ToList(),
        };
    }

    private static string _FirebaseMessagingExceptionToString(FirebaseMessagingException exception)
    {
        return $"MessagingErrorCode: {exception.MessagingErrorCode} (ErrorCode: {exception.ErrorCode}){Environment.NewLine}"
            + $"Message: {exception.Message}{Environment.NewLine}"
            + exception;
    }
}
