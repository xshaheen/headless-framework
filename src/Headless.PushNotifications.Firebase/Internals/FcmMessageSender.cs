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
/// <remarks>
/// The <c>optionsName</c> constructor argument is the setup-builder instance name (<see langword="null"/> for
/// the default unkeyed sender). Every factory reads the options snapshot for its own name
/// (<c>IOptionsMonitor.Get(optionsName)</c>) and its own retry pipeline (keyed by the same name) so keyed
/// settings never bleed across instances — a keyed sender must not read <c>CurrentValue</c>, which binds the
/// default.
/// </remarks>
internal sealed class FcmMessageSender : IFcmMessageSender, IDisposable
{
    private const int _ApnsBadge = 1;

    private readonly ILogger<FcmMessageSender> _logger;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly Lazy<FirebaseMessaging> _messaging;
    private FirebaseApp? _app;

    public FcmMessageSender(
        IOptionsMonitor<FirebaseOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        string? optionsName,
        ILogger<FcmMessageSender> logger
    )
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(pipelineProvider);
        _logger = Argument.IsNotNull(logger);
        _retryPipeline = pipelineProvider.GetPipeline(FcmResilienceKeys.GetRetryPipelineKey(optionsName));

        var json = options.Get(optionsName).Json;
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
        string fid,
        CancellationToken cancellationToken
    )
    {
        var message = BuildMessage(content, fid);

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

            return PushNotificationResponse.Succeeded(fid, messageId);
        }
        catch (FirebaseMessagingException e) when (e.MessagingErrorCode is MessagingErrorCode.Unregistered)
        {
            return PushNotificationResponse.Unregistered(fid);
        }
        catch (FirebaseMessagingException e)
        {
            _logger.FailedToSendPushNotification(e, _Mask(fid));

            return PushNotificationResponse.Failed(fid, _Describe(e));
        }
    }

    public async Task<IReadOnlyList<PushNotificationResponse>> SendBatchAsync(
        FcmMessageContent content,
        IReadOnlyList<string> fids,
        CancellationToken cancellationToken
    )
    {
        var message = BuildMulticastMessage(content, fids);

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
            // Whole-batch transport failure after retries: report every FID as failed so the caller still
            // receives a complete result set and earlier batches are not discarded.
            _logger.FailedToSendPushNotification(e, $"multicast:{fids.Count}");

            var error = _Describe(e);
            var failed = new List<PushNotificationResponse>(fids.Count);
            foreach (var fid in fids)
            {
                failed.Add(PushNotificationResponse.Failed(fid, error));
            }

            return failed;
        }

        if (batchResponse.Responses.Count != fids.Count)
        {
            throw new InvalidOperationException(
                $"Firebase response count ({batchResponse.Responses.Count}) does not match FID count ({fids.Count})."
            );
        }

        var results = new List<PushNotificationResponse>(fids.Count);
        for (var i = 0; i < fids.Count; i++)
        {
            var response = batchResponse.Responses[i];
            var fid = fids[i];

            if (response.IsSuccess)
            {
                results.Add(PushNotificationResponse.Succeeded(fid, response.MessageId));
            }
            else if (response.Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered)
            {
                results.Add(PushNotificationResponse.Unregistered(fid));
            }
            else
            {
                results.Add(PushNotificationResponse.Failed(fid, _Describe(response.Exception)));
            }
        }

        return results;
    }

    internal static Message BuildMessage(FcmMessageContent content, string fid)
    {
        return new Message
        {
            Fid = fid,
            Data = content.Data,
            Notification = new Notification { Title = content.Title, Body = content.Body },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig { Aps = new Aps { Badge = _ApnsBadge } },
        };
    }

    internal static MulticastMessage BuildMulticastMessage(FcmMessageContent content, IReadOnlyList<string> fids)
    {
        return new MulticastMessage
        {
            Fids = [.. fids],
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

    private static string _Mask(string clientIdentifier)
    {
        return clientIdentifier.Length > 8 ? clientIdentifier[..8] + "***" : "***";
    }

    public void Dispose()
    {
        if (_messaging.IsValueCreated)
        {
            _app?.Delete();
        }
    }
}
