// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>
/// Default <see cref="IFcmMessageSender"/> backed by the FirebaseAdmin SDK. Creates a uniquely-named
/// <see cref="FirebaseApp"/> lazily on first send (so registration has no side effects and several hosts can
/// coexist in one process with different credentials), and disposes it with the container.
/// </summary>
internal sealed class FcmMessageSender : IFcmMessageSender, IDisposable
{
    private const int _ApnsBadge = 1;

    private readonly ILogger<FcmMessageSender> _logger;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly Lazy<FirebaseMessaging> _messaging;
    private FirebaseApp? _app;

    public FcmMessageSender(
        IOptions<FirebaseOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<FcmMessageSender> logger
    )
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(pipelineProvider);
        _logger = Argument.IsNotNull(logger);
        _retryPipeline = pipelineProvider.GetPipeline(FcmResilienceKeys.RetryPipeline);

        var json = options.Value.Json;
        var appName = "Headless.PushNotifications.Firebase." + Guid.NewGuid().ToString("N");

        _messaging = new Lazy<FirebaseMessaging>(() =>
        {
            _app = FirebaseApp.Create(
                new AppOptions
                {
                    Credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential(),
                },
                appName
            );

            return FirebaseMessaging.GetMessaging(_app);
        });
    }

    public async Task<PushNotificationResponse> SendAsync(
        FcmMessageContent content,
        string token,
        CancellationToken cancellationToken
    )
    {
        var message = _BuildMessage(content, token);

        try
        {
            var messageId = await _retryPipeline
                .ExecuteAsync(
                    static async (state, ct) =>
                        await state.messaging.Value.SendAsync(state.message, ct).ConfigureAwait(false),
                    (messaging: _messaging, message),
                    cancellationToken
                )
                .ConfigureAwait(false);

            return PushNotificationResponse.Succeeded(token, messageId);
        }
        catch (FirebaseMessagingException e) when (e.MessagingErrorCode is MessagingErrorCode.Unregistered)
        {
            return PushNotificationResponse.Unregistered(token);
        }
        catch (FirebaseMessagingException e)
        {
            _logger.FailedToSendPushNotification(e, _Mask(token));

            return PushNotificationResponse.Failed(token, _Describe(e));
        }
    }

    public async Task<IReadOnlyList<PushNotificationResponse>> SendBatchAsync(
        FcmMessageContent content,
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken
    )
    {
        var message = _BuildMulticastMessage(content, tokens);

        BatchResponse batchResponse;

        try
        {
            batchResponse = await _retryPipeline
                .ExecuteAsync(
                    static async (state, ct) =>
                        await state.messaging.Value.SendEachForMulticastAsync(state.message, ct).ConfigureAwait(false),
                    (messaging: _messaging, message),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (FirebaseMessagingException e)
        {
            // Whole-batch transport failure after retries: report every token as failed so the caller still
            // receives a complete result set and earlier batches are not discarded.
            _logger.FailedToSendPushNotification(e, $"multicast:{tokens.Count}");

            var error = _Describe(e);
            var failed = new List<PushNotificationResponse>(tokens.Count);
            foreach (var token in tokens)
            {
                failed.Add(PushNotificationResponse.Failed(token, error));
            }

            return failed;
        }

        if (batchResponse.Responses.Count != tokens.Count)
        {
            throw new InvalidOperationException(
                $"Firebase response count ({batchResponse.Responses.Count}) does not match token count ({tokens.Count})."
            );
        }

        var results = new List<PushNotificationResponse>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var response = batchResponse.Responses[i];
            var token = tokens[i];

            if (response.IsSuccess)
            {
                results.Add(PushNotificationResponse.Succeeded(token, response.MessageId));
            }
            else if (response.Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered)
            {
                results.Add(PushNotificationResponse.Unregistered(token));
            }
            else
            {
                results.Add(PushNotificationResponse.Failed(token, _Describe(response.Exception)));
            }
        }

        return results;
    }

    private static Message _BuildMessage(FcmMessageContent content, string token)
    {
        return new Message
        {
            Token = token,
            Data = content.Data,
            Notification = new Notification { Title = content.Title, Body = content.Body },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig { Aps = new Aps { Badge = _ApnsBadge } },
        };
    }

    private static MulticastMessage _BuildMulticastMessage(FcmMessageContent content, IReadOnlyList<string> tokens)
    {
        return new MulticastMessage
        {
            Tokens = [.. tokens],
            Data = content.Data,
            Notification = new Notification { Title = content.Title, Body = content.Body },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig { Aps = new Aps { Badge = _ApnsBadge } },
        };
    }

    private static string _Describe(FirebaseMessagingException? exception)
    {
        return exception is null ? "Unknown error" : $"{exception.MessagingErrorCode}: {exception.Message}";
    }

    private static string _Mask(string token)
    {
        return token.Length > 8 ? token[..8] + "***" : "***";
    }

    public void Dispose()
    {
        if (_messaging.IsValueCreated)
        {
            _app?.Delete();
        }
    }
}
