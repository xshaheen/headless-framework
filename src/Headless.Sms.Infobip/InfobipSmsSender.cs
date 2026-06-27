// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
            logger.LogSmsSentSuccessfully(destinationCount: 1);

            return SendSingleSmsResponse.Succeeded(smsResponse.BulkId);
        }
        catch (ApiException e)
        {
            logger.LogSmsSendFailed(e, destinationCount: 1, e.ErrorCode);

            return SendSingleSmsResponse.Failed(_FormatApiError(e));
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
                SendSingleSmsResponse.Failed(_FormatApiError(e))
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

        if (messages is null || messages.Count != destinations.Count)
        {
            var aggregate = SendSingleSmsResponse.Succeeded(response.BulkId);

            return SendBulkSmsResponse.FromAggregate(destinations, aggregate, response.BulkId);
        }

        var results = new List<SmsRecipientResult>(destinations.Count);

        for (var i = 0; i < destinations.Count; i++)
        {
            results.Add(new SmsRecipientResult(destinations[i], _MapMessageResponse(messages[i])));
        }

        return SendBulkSmsResponse.FromResults(results, response.BulkId);
    }

    private static SendSingleSmsResponse _MapMessageResponse(SmsResponseDetails message)
    {
        var status = message.Status;
        var groupName = status?.GroupName;

        if (
            groupName is MessageGeneralStatus.Accepted or MessageGeneralStatus.Pending or MessageGeneralStatus.Delivered
        )
        {
            return SendSingleSmsResponse.Succeeded(message.MessageId);
        }

        var failure = string.IsNullOrWhiteSpace(status?.Description)
            ? $"Infobip message status {status?.Name ?? groupName?.ToString() ?? "unknown"}"
            : status.Description;

        return SendSingleSmsResponse.Failed(failure, _MapMessageFailureKind(status));
    }

    private static SmsFailureKind _MapMessageFailureKind(SmsMessageStatus? status)
    {
        if (status is null)
        {
            return SmsFailureKind.Unknown;
        }

        if (status.Name?.Contains("BALANCE", StringComparison.OrdinalIgnoreCase) == true)
        {
            return SmsFailureKind.OutOfCredit;
        }

        if (status.Name?.Contains("AUTH", StringComparison.OrdinalIgnoreCase) == true)
        {
            return SmsFailureKind.AuthFailure;
        }

        if (status.Name?.Contains("RATE", StringComparison.OrdinalIgnoreCase) == true)
        {
            return SmsFailureKind.RateLimited;
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
