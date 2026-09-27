// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Headless.Checks;
using Headless.PushNotifications.Firebase.Internals;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Firebase Cloud Messaging (FCM) push notification service. Validates input, splits multicast sends into
/// FCM-sized batches, and aggregates per-target outcomes. All FCM interaction, transient-failure retry, and
/// telemetry is delegated to <see cref="IFcmMessageSender"/>.
/// </summary>
/// <remarks>
/// One instance serves both <see cref="IFcmPushNotificationService"/> and <see cref="IPushNotificationService"/>. The
/// shared methods convert the request into an <see cref="FcmMessage"/>, send it through the typed path, and return
/// each result's <see cref="FcmSendResult.Response"/>.
/// </remarks>
internal sealed class FcmPushNotificationService(IFcmMessageSender sender, TimeProvider timeProvider)
    : IFcmPushNotificationService,
        IPushNotificationService
{
    private const int _MaxFidsPerBatch = 500;

    // Forwarded to APNs as apns-collapse-id through the iOS bridge, and Apple caps that header at 64 UTF-8 bytes.
    private const int _MaxCollapseKeyBytes = 64;

    private const string _ApnsNormalPriority = "5";
    private const string _ApnsHighPriority = "10";

    #region Typed

    public async ValueTask<FcmSendResult> SendAsync(
        string fid,
        FcmMessage message,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(fid);
        FcmMessageValidation.Validate(message);

        return await sender.SendAsync(message, FcmTarget.Token(fid), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FcmBatchSendResult> SendMulticastAsync(
        IReadOnlyList<string> fids,
        FcmMessage message,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(fids);

        foreach (var fid in fids)
        {
            Argument.IsNotNullOrWhiteSpace(fid, "A multicast FID must not be blank.", nameof(fids));
        }

        FcmMessageValidation.Validate(message);

        var results = await _SendManyAsync(message, fids, cancellationToken).ConfigureAwait(false);
        var successCount = results.Count(static r => r.Response.IsSucceeded());

        return new FcmBatchSendResult
        {
            SuccessCount = successCount,
            FailureCount = results.Count - successCount,
            Results = results,
        };
    }

    public async ValueTask<FcmSendResult> SendToTopicAsync(
        string topic,
        FcmMessage message,
        CancellationToken cancellationToken = default
    )
    {
        FcmMessageValidation.ValidateTopic(topic);
        FcmMessageValidation.Validate(message);

        return await sender.SendAsync(message, FcmTarget.Topic(topic), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FcmSendResult> SendToConditionAsync(
        string condition,
        FcmMessage message,
        CancellationToken cancellationToken = default
    )
    {
        FcmMessageValidation.ValidateCondition(condition);
        FcmMessageValidation.Validate(message);

        return await sender.SendAsync(message, FcmTarget.Condition(condition), cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Shared

    public async ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientIdentifier,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(clientIdentifier);

        var message = ToFcmMessage(request, timeProvider.GetUtcNow());
        var result = await sender
            .SendAsync(message, FcmTarget.Token(clientIdentifier), cancellationToken)
            .ConfigureAwait(false);

        return result.Response;
    }

    public async ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(clientIdentifiers);

        var message = ToFcmMessage(request, timeProvider.GetUtcNow());
        var results = await _SendManyAsync(message, clientIdentifiers, cancellationToken).ConfigureAwait(false);
        var responses = results.Select(static r => r.Response).ToArray();
        var successCount = responses.Count(static r => r.IsSucceeded());

        return new BatchPushNotificationResponse
        {
            SuccessCount = successCount,
            FailureCount = responses.Length - successCount,
            Responses = responses,
        };
    }

    /// <summary>
    /// Validates a shared request and converts it into the FCM message it becomes: the notification or data message,
    /// the Android delivery fields, and the APNs bridge headers and <c>aps</c> dictionary. <paramref name="now"/>
    /// anchors the absolute APNs expiration, so every retry of the send carries the same one.
    /// </summary>
    internal static FcmMessage ToFcmMessage(PushNotificationRequest request, DateTimeOffset now)
    {
        // FCM limits only the whole payload (4096 bytes) and rejects an oversized one with INVALID_ARGUMENT, so
        // title and body lengths are not checked here.
        PushNotificationRequestValidation.Validate(request);
        FcmMessageValidation.EnsureDataAllowed(request.Data, "request.Data");

        if (request.CollapseKey is not null)
        {
            Argument.IsLessThanOrEqualTo(
                Encoding.UTF8.GetByteCount(request.CollapseKey),
                _MaxCollapseKeyBytes,
                "A collapse key cannot exceed 64 UTF-8 bytes.",
                "request.CollapseKey"
            );
        }

        var dataOnly = PushNotificationRequestValidation.IsDataOnly(request);

        return new FcmMessage
        {
            // Without a notification block FCM delivers a data message, which the app handles in the background.
            Notification = dataOnly ? null : new FcmNotification { Title = request.Title, Body = request.Body },
            Data = request.Data,
            Android = new FcmAndroidOptions
            {
                // High stays the default so callers that never set a priority keep today's delivery.
                Priority =
                    request.Priority is PushNotificationPriority.Normal
                        ? FcmAndroidPriority.Normal
                        : FcmAndroidPriority.High,
                CollapseKey = request.CollapseKey,
                TimeToLive = request.TimeToLive,
                // Android cannot clear a badge, so a zero count means "not set" there, as it does in FCM itself.
                NotificationCount = request.Badge is > 0 ? request.Badge : null,
                Sound = request.Sound,
            },
            Apns = _ToApns(request, dataOnly, now),
        };
    }

    private static FcmApnsOptions? _ToApns(PushNotificationRequest request, bool dataOnly, DateTimeOffset now)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (request.CollapseKey is not null)
        {
            headers["apns-collapse-id"] = request.CollapseKey;
        }

        if (dataOnly)
        {
            // Apple requires priority 5 for a background (content-available) push and rejects 10 for it.
            headers["apns-priority"] = _ApnsNormalPriority;
        }
        else if (request.Priority is { } priority)
        {
            headers["apns-priority"] =
                priority is PushNotificationPriority.High ? _ApnsHighPriority : _ApnsNormalPriority;
        }

        if (request.TimeToLive is { } timeToLive)
        {
            // APNs reads 0 as "attempt once, do not store", so a zero lifetime must not become the current time.
            headers["apns-expiration"] =
                timeToLive == TimeSpan.Zero
                    ? "0"
                    : (now + timeToLive).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        var aps = _ToAps(request, dataOnly);

        if (headers.Count == 0 && aps is null)
        {
            return null;
        }

        return new FcmApnsOptions
        {
            Headers = headers.Count == 0 ? null : headers,
            Payload = aps is null ? null : new JsonObject { ["aps"] = aps },
        };
    }

    private static JsonObject? _ToAps(PushNotificationRequest request, bool dataOnly)
    {
        if (dataOnly)
        {
            return new JsonObject { ["content-available"] = 1 };
        }

        if (request.Badge is null && request.Sound is null)
        {
            return null;
        }

        var aps = new JsonObject();

        if (request.Badge is { } badge)
        {
            aps["badge"] = badge;
        }

        if (request.Sound is { } sound)
        {
            aps["sound"] = sound;
        }

        return aps;
    }

    #endregion

    private async Task<IReadOnlyList<FcmSendResult>> _SendManyAsync(
        FcmMessage message,
        IReadOnlyList<string> fids,
        CancellationToken cancellationToken
    )
    {
        var results = new List<FcmSendResult>(fids.Count);

        foreach (var batch in fids.Chunk(_MaxFidsPerBatch))
        {
            results.AddRange(await sender.SendBatchAsync(message, batch, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
