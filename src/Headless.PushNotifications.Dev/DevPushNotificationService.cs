// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Context;

namespace Headless.PushNotifications.Dev;

/// <summary>
/// Captures each push notification in <see cref="IPushNotificationCaptureStore"/> instead of delivering it.
/// </summary>
/// <remarks>
/// It validates the request with the same provider-neutral rules as the real providers and throws
/// <see cref="ArgumentException"/> for a malformed one, after capturing it with its error. A payload estimated over
/// <see cref="CapturedPushNotification.MaxPayloadBytes"/> is flagged in
/// <see cref="CapturedPushNotification.ExceedsPayloadLimit"/> but still succeeds: the estimate is not a provider's own
/// serialization, so failing on it could reject a payload the provider accepts. Nothing reaches a device.
/// </remarks>
internal sealed class DevPushNotificationService(
    PushNotificationCaptureStore store,
    string senderName = "",
    ICorrelationIdProvider? correlationIdProvider = null
) : IPushNotificationService
{
    private readonly PushNotificationCaptureStore _store = Argument.IsNotNull(store);
    private readonly string _senderName = Argument.IsNotNull(senderName);

    public ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientIdentifier,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var captured = _Capture([clientIdentifier], isMulticast: false, request);

        return ValueTask.FromResult(_Respond(captured, clientIdentifier));
    }

    public ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(clientIdentifiers);
        cancellationToken.ThrowIfCancellationRequested();

        var captured = _Capture([.. clientIdentifiers], isMulticast: true, request);
        var responses = clientIdentifiers.Select(id => _Respond(captured, id)).ToList();
        var successCount = responses.Count(static r => r.IsSucceeded());

        return ValueTask.FromResult(
            new BatchPushNotificationResponse
            {
                SuccessCount = successCount,
                FailureCount = responses.Count - successCount,
                Responses = responses,
            }
        );
    }

    private static PushNotificationResponse _Respond(CapturedPushNotification captured, string clientIdentifier)
    {
        return captured.Succeeded
            ? PushNotificationResponse.Succeeded(clientIdentifier, captured.Id.ToString())
            : PushNotificationResponse.Failed(clientIdentifier, captured.FailureError);
    }

    private CapturedPushNotification _Capture(
        string[] clientIdentifiers,
        bool isMulticast,
        PushNotificationRequest request
    )
    {
        Argument.IsNotNull(request);

        try
        {
            foreach (var clientIdentifier in clientIdentifiers)
            {
                Argument.IsNotNullOrWhiteSpace(clientIdentifier);
            }

            PushNotificationRequestValidation.Validate(request);
        }
        catch (ArgumentException e)
        {
            _Store(clientIdentifiers, isMulticast, request, payloadSizeBytes: 0, e.Message);

            throw;
        }

        var payloadSizeBytes = PushNotificationPayload.MeasureBytes(request);

        return _Store(clientIdentifiers, isMulticast, request, payloadSizeBytes, failureError: null);
    }

    private CapturedPushNotification _Store(
        string[] clientIdentifiers,
        bool isMulticast,
        PushNotificationRequest request,
        int payloadSizeBytes,
        string? failureError
    )
    {
        return _store.Add(
            new CapturedPushNotification
            {
                SenderName = _senderName,
                ClientIdentifiers = clientIdentifiers,
                IsMulticast = isMulticast,
                Title = request.Title,
                Body = request.Body,
                IsDataOnly = PushNotificationRequestValidation.IsDataOnly(request),
                // Copy the caller's dictionary so a later change to it leaves the capture intact.
                Data = request.Data is null
                    ? null
                    : new Dictionary<string, string>(request.Data, StringComparer.Ordinal),
                CollapseKey = request.CollapseKey,
                Badge = request.Badge,
                Sound = request.Sound,
                Priority = request.Priority,
                TimeToLive = request.TimeToLive,
                PayloadSizeBytes = payloadSizeBytes,
                FailureError = failureError,
                TraceId = Activity.Current?.TraceId.ToString(),
                CorrelationId = correlationIdProvider?.CorrelationId,
            }
        );
    }
}
