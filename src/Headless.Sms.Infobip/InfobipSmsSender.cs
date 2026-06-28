// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Infobip.Api.Client;
using Infobip.Api.Client.Api;
using Infobip.Api.Client.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Infobip;

internal sealed class InfobipSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<InfobipSmsOptions> optionsAccessor,
    ILogger<InfobipSmsSender> logger
) : ISmsSender, IBulkSmsSender
{
    private readonly InfobipSmsOptions _options = optionsAccessor.Value;

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        var destination = new SmsDestination(
            to: request.Destination.ToString(hasPlusPrefix: false),
            messageId: request.MessageId
        );

        try
        {
            var smsResponse = await _SendAsync([destination], request.Text, cancellationToken).ConfigureAwait(false);

            // A single send returns one message entry; honor its per-recipient status so a rejection delivered
            // inside a 200 response is reported as a failure (matching the bulk path) instead of a blanket
            // success. The success id stays the bulk id, as documented.
            var message = smsResponse.Messages is { Count: > 0 } ? smsResponse.Messages[0] : null;

            if (message is null || _IsAccepted(message.Status))
            {
                logger.LogSmsSentSuccessfully(destinationCount: 1);

                return SendSingleSmsResponse.Succeeded(smsResponse.BulkId);
            }

            return _MapMessageResponse(message);
        }
        catch (ApiException e)
        {
            logger.LogSmsSendFailed(e, destinationCount: 1, e.ErrorCode);

            return SendSingleSmsResponse.Failed(
                _FormatApiError(e),
                SmsFailureKinds.FromHttpStatusCode((HttpStatusCode)e.ErrorCode)
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, destinationCount: 1);

            return SendSingleSmsResponse.FromException(e);
        }
    }

    public async ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destinations);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        var destinations = request
            .Destinations.Select(
                (item, index) =>
                {
                    var messageId = request.MessageId is null
                        ? null
                        : request.MessageId + (index + 1).ToString(CultureInfo.InvariantCulture);

                    return new SmsDestination(to: item.ToString(hasPlusPrefix: false), messageId: messageId);
                }
            )
            .ToList();

        try
        {
            var smsResponse = await _SendAsync(destinations, request.Text, cancellationToken).ConfigureAwait(false);
            logger.LogSmsSentSuccessfully(request.Destinations.Count);

            return _MapBulkResponse(request.Destinations, smsResponse);
        }
        catch (ApiException e)
        {
            logger.LogSmsSendFailed(e, request.Destinations.Count, e.ErrorCode);

            return SendBulkSmsResponse.FromAggregate(
                request.Destinations,
                SendSingleSmsResponse.Failed(
                    _FormatApiError(e),
                    SmsFailureKinds.FromHttpStatusCode((HttpStatusCode)e.ErrorCode)
                )
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, request.Destinations.Count);

            return SendBulkSmsResponse.FromAggregate(request.Destinations, SendSingleSmsResponse.FromException(e));
        }
    }

    private async Task<SmsResponse> _SendAsync(
        List<SmsDestination> destinations,
        string text,
        CancellationToken cancellationToken
    )
    {
        var smsMessage = new SmsMessage(_options.Sender, destinations, new SmsMessageContent(new SmsTextContent(text)));
        var smsRequest = new SmsRequest([smsMessage]);

        using var httpClient = httpClientFactory.CreateClient(SetupInfobip.HttpClientName);
        using var smsApi = new SmsApi(
            httpClient,
            new Configuration { BasePath = _options.BasePath, ApiKey = _options.ApiKey }
        );

        return await smsApi.SendSmsMessagesAsync(smsRequest, cancellationToken).ConfigureAwait(false);
    }

    // Infobip returns one entry per recipient (in request order) carrying its own message id and status.
    // When that detail is present we map it per recipient; otherwise we fall back to the bulk id.
    private static SendBulkSmsResponse _MapBulkResponse(
        IReadOnlyList<SmsRequestDestination> destinations,
        SmsResponse response
    )
    {
        var messages = response.Messages;

        if (messages is null)
        {
            // A 200 with a bulk id but no per-recipient breakdown: the batch was accepted, so mirror the
            // aggregate success to every recipient.
            return SendBulkSmsResponse.FromAggregate(
                destinations,
                SendSingleSmsResponse.Succeeded(response.BulkId),
                response.BulkId
            );
        }

        if (messages.Count != destinations.Count)
        {
            // The provider returned a per-recipient breakdown whose count does not match the request, so
            // positional mapping is unsafe. Do not fabricate success: report the outcome as Unknown for every
            // recipient so callers can retry or investigate rather than treat unconfirmed sends as accepted.
            return SendBulkSmsResponse.FromAggregate(
                destinations,
                SendSingleSmsResponse.Failed(
                    $"Infobip returned {messages.Count} message result(s) for {destinations.Count} recipient(s)",
                    SmsFailureKind.Unknown
                ),
                response.BulkId
            );
        }

        var results = new List<SmsRecipientResult>(destinations.Count);

        for (var i = 0; i < destinations.Count; i++)
        {
            results.Add(new SmsRecipientResult(destinations[i], _MapMessageResponse(messages[i])));
        }

        return SendBulkSmsResponse.FromResults(results, response.BulkId);
    }

    // Infobip's PENDING/ACCEPTED/DELIVERED groups mean the message was taken for delivery; everything else is
    // a rejection the caller should treat as a failed send.
    private static bool _IsAccepted(SmsMessageStatus? status) =>
        status?.GroupName
            is MessageGeneralStatus.Accepted
                or MessageGeneralStatus.Pending
                or MessageGeneralStatus.Delivered;

    private static SendSingleSmsResponse _MapMessageResponse(SmsResponseDetails message)
    {
        if (_IsAccepted(message.Status))
        {
            return SendSingleSmsResponse.Succeeded(message.MessageId);
        }

        var status = message.Status;
        var failure = string.IsNullOrWhiteSpace(status?.Description)
            ? $"Infobip message status {status?.Name ?? status?.GroupName?.ToString() ?? "unknown"}"
            : status.Description;

        return SendSingleSmsResponse.Failed(failure, _MapMessageFailureKind(status));
    }

    private static SmsFailureKind _MapMessageFailureKind(SmsMessageStatus? status)
    {
        if (status is null)
        {
            return SmsFailureKind.Unknown;
        }

        // Infobip exposes the specific reason only as a free-form per-message status name (for example
        // "REJECTED_NOT_ENOUGH_CREDITS"). Authentication (401) and rate-limit (429) failures surface at the
        // request level as an ApiException, not as per-message statuses, so out-of-credit is the only kind that
        // can be refined here; every other rejection falls back to the delivery group.
        if (status.Name?.Contains("CREDIT", StringComparison.OrdinalIgnoreCase) == true)
        {
            return SmsFailureKind.OutOfCredit;
        }

        return status.GroupName switch
        {
            MessageGeneralStatus.Undeliverable or MessageGeneralStatus.Expired or MessageGeneralStatus.Rejected =>
                SmsFailureKind.InvalidRecipient,
            _ => SmsFailureKind.Unknown,
        };
    }

    private static string _FormatApiError(ApiException exception)
    {
        FormattableString error = $"ErrorCode: {exception.ErrorCode} {exception.Message}";

        return error.ToInvariantString();
    }
}
