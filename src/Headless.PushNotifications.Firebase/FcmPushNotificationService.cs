// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FirebaseAdmin.Messaging;
using Headless.Checks;
using Headless.PushNotifications;
using Headless.PushNotifications.Abstractions;
using Headless.PushNotifications.Firebase.Internals;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Firebase Cloud Messaging (FCM) push notification service implementation.
/// </summary>
/// <remarks>
/// Implements automatic retry for transient FCM failures (rate limits, temporary outages, server errors)
/// using exponential backoff with jitter. Permanent errors (invalid tokens, malformed requests) return immediately.
/// Retry behavior configurable via <see cref="FirebaseOptions.Retry"/>.
/// </remarks>
public sealed class FcmPushNotificationService(
    ILogger<FcmPushNotificationService> logger,
    ResiliencePipelineProvider<string> pipelineProvider
) : IPushNotificationService
{
    private const int _MaxTitleLength = 100;
    private const int _MaxBodyLength = 4000;
    private const int _MaxTokensPerBatch = 500;

    private readonly ResiliencePipeline _retryPipeline = pipelineProvider.GetPipeline("Headless:FcmRetry");

    public async ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(clientToken);
        Argument.IsNotNullOrWhiteSpace(title);
        Argument.IsNotNullOrWhiteSpace(body);

        if (title.Length > _MaxTitleLength)
        {
            throw new ArgumentException($"Title exceeds maximum length of {_MaxTitleLength} characters", nameof(title));
        }

        if (body.Length > _MaxBodyLength)
        {
            throw new ArgumentException($"Body exceeds maximum length of {_MaxBodyLength} characters", nameof(body));
        }

        _EnsureDataNotContainReservedWords(data);

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
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            context.Properties.Set(new ResiliencePropertyKey<ILogger>("logger"), logger);

            try
            {
                var messageId = await _retryPipeline
                    .ExecuteAsync(
                        static async (ctx, state) =>
                        {
                            var (msg, ct) = state;
                            return await FirebaseMessaging.DefaultInstance.SendAsync(msg, ct).ConfigureAwait(false);
                        },
                        context,
                        (message, cancellationToken)
                    )
                    .ConfigureAwait(false);

                return PushNotificationResponse.Succeeded(clientToken, messageId);
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
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
            logger.FailedToSendPushNotification(e, clientToken.Length > 8 ? clientToken[..8] + "***" : "***");

            return PushNotificationResponse.Failed(clientToken, "An internal error occurred");
        }
    }

    public async ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(clientTokens);
        Argument.IsNotNullOrWhiteSpace(title);
        Argument.IsNotNullOrWhiteSpace(body);

        if (title.Length > _MaxTitleLength)
        {
            throw new ArgumentException($"Title exceeds maximum length of {_MaxTitleLength} characters", nameof(title));
        }

        if (body.Length > _MaxBodyLength)
        {
            throw new ArgumentException($"Body exceeds maximum length of {_MaxBodyLength} characters", nameof(body));
        }

        _EnsureDataNotContainReservedWords(data);

        var allResponses = new List<PushNotificationResponse>(clientTokens.Count);
        var successCount = 0;
        var failureCount = 0;

        try
        {
            foreach (var batch in clientTokens.Chunk(_MaxTokensPerBatch))
            {
                var batchList = batch.ToList();
                var message = new MulticastMessage
                {
                    Tokens = batchList,
                    Data = data,
                    Notification = new Notification { Title = title, Body = body },
                    Android = new AndroidConfig { Priority = Priority.High },
                    Apns = new ApnsConfig { Aps = new Aps { Badge = 1 } },
                };

                var context = ResilienceContextPool.Shared.Get(cancellationToken);
                context.Properties.Set(new ResiliencePropertyKey<ILogger>("logger"), logger);

                try
                {
                    var batchResponse = await _retryPipeline
                        .ExecuteAsync(
                            static async (ctx, state) =>
                            {
                                var (msg, ct) = state;
                                return await FirebaseMessaging
                                    .DefaultInstance.SendEachForMulticastAsync(msg, ct)
                                    .ConfigureAwait(false);
                            },
                            context,
                            (message, cancellationToken)
                        )
                        .ConfigureAwait(false);

                    if (batchResponse.Responses.Count != batchList.Count)
                    {
                        throw new InvalidOperationException(
                            $"Firebase response count ({batchResponse.Responses.Count}) does not match token count ({batchList.Count})"
                        );
                    }

                    successCount += batchResponse.SuccessCount;
                    failureCount += batchResponse.FailureCount;

                    for (var i = 0; i < batchResponse.Responses.Count; i++)
                    {
                        var response = batchResponse.Responses[i];
                        var token = batchList[i];

                        if (response.IsSuccess)
                        {
                            allResponses.Add(PushNotificationResponse.Succeeded(token, response.MessageId));
                        }
                        else if (response.Exception.MessagingErrorCode == MessagingErrorCode.Unregistered)
                        {
                            allResponses.Add(PushNotificationResponse.Unregistered(token));
                        }
                        else
                        {
                            allResponses.Add(
                                PushNotificationResponse.Failed(
                                    token,
                                    _FirebaseMessagingExceptionToString(response.Exception)
                                )
                            );
                        }
                    }
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(context);
                }
            }
        }
        catch (Exception e)
        {
            logger.FailedToSendPushNotification(e, $"multicast:{clientTokens.Count}");
            throw;
        }

        return new BatchPushNotificationResponse
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            Responses = allResponses,
        };
    }

    private static void _EnsureDataNotContainReservedWords(IReadOnlyDictionary<string, string>? data)
    {
        if (
            data is not null
            && (data.ContainsKey("from") || data.ContainsKey("notification") || data.ContainsKey("message_type"))
        )
        {
            throw new InvalidOperationException("Notification data contain reserved word(s).");
        }
    }

    private static string _FirebaseMessagingExceptionToString(FirebaseMessagingException exception)
    {
        return $"MessagingErrorCode: {exception.MessagingErrorCode}";
    }
}
