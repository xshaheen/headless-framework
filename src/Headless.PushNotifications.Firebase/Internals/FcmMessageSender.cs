// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
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
    private const string _ApnsNormalPriority = "5";
    private const string _ApnsHighPriority = "10";

    private readonly ILogger<FcmMessageSender> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly Lazy<FirebaseMessaging> _messaging;
    private FirebaseApp? _app;

    public FcmMessageSender(
        IOptionsMonitor<FirebaseOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        string? optionsName,
        TimeProvider timeProvider,
        ILogger<FcmMessageSender> logger
    )
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(pipelineProvider);
        _timeProvider = Argument.IsNotNull(timeProvider);
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
        var message = BuildMessage(content, fid, _timeProvider.GetUtcNow());

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
        var message = BuildMulticastMessage(content, fids, _timeProvider.GetUtcNow());

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

    internal static Message BuildMessage(FcmMessageContent content, string fid, DateTimeOffset now)
    {
        return new Message
        {
            Fid = fid,
            Data = content.Data,
            Notification = _BuildNotification(content),
            Android = _BuildAndroidConfig(content),
            Apns = _BuildApnsConfig(content, now),
        };
    }

    internal static MulticastMessage BuildMulticastMessage(
        FcmMessageContent content,
        IReadOnlyList<string> fids,
        DateTimeOffset now
    )
    {
        return new MulticastMessage
        {
            Fids = [.. fids],
            Data = content.Data,
            Notification = _BuildNotification(content),
            Android = _BuildAndroidConfig(content),
            Apns = _BuildApnsConfig(content, now),
        };
    }

    private static Notification? _BuildNotification(FcmMessageContent content)
    {
        // Without a notification block FCM delivers a data message, which the app handles in the background.
        return content.IsDataOnly ? null : new Notification { Title = content.Title, Body = content.Body };
    }

    private static AndroidConfig _BuildAndroidConfig(FcmMessageContent content)
    {
        // Android cannot clear a badge, so a zero count means "not set" there, as it does in FCM itself.
        var count = content.Badge is > 0 ? content.Badge : null;

        return new AndroidConfig
        {
            // High stays the default so callers that never set a priority keep today's delivery.
            Priority = content.Priority is PushNotificationPriority.Normal ? Priority.Normal : Priority.High,
            CollapseKey = content.CollapseKey,
            TimeToLive = content.TimeToLive,
            Notification =
                count is null && content.Sound is null
                    ? null
                    : new AndroidNotification { NotificationCount = count, Sound = content.Sound },
        };
    }

    private static ApnsConfig _BuildApnsConfig(FcmMessageContent content, DateTimeOffset now)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (content.CollapseKey is not null)
        {
            headers["apns-collapse-id"] = content.CollapseKey;
        }

        if (content.IsDataOnly)
        {
            // Apple requires priority 5 for a background (content-available) push and rejects 10 for it.
            headers["apns-priority"] = _ApnsNormalPriority;
        }
        else if (content.Priority is { } priority)
        {
            headers["apns-priority"] =
                priority is PushNotificationPriority.High ? _ApnsHighPriority : _ApnsNormalPriority;
        }

        if (content.TimeToLive is { } timeToLive)
        {
            // APNs reads 0 as "attempt once, do not store", so a zero lifetime must not become the current time.
            headers["apns-expiration"] =
                timeToLive == TimeSpan.Zero
                    ? "0"
                    : (now + timeToLive).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        return new ApnsConfig { Aps = _BuildAps(content), Headers = headers.Count == 0 ? null : headers };
    }

    private static Aps? _BuildAps(FcmMessageContent content)
    {
        if (content.IsDataOnly)
        {
            return new Aps { ContentAvailable = true };
        }

        if (content.Badge is null && content.Sound is null)
        {
            return null;
        }

        return new Aps { Badge = content.Badge, Sound = content.Sound };
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
