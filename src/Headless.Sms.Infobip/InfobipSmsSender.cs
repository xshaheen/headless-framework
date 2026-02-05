// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Infobip.Api.Client;
using Infobip.Api.Client.Api;
using Infobip.Api.Client.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Infobip;

public sealed class InfobipSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<InfobipSmsOptions> optionsAccessor,
    ILogger<InfobipSmsSender> logger
) : ISmsSender
{
    internal const string HttpClientName = "Headless:InfobipSms";

    private readonly InfobipSmsOptions _options = optionsAccessor.Value;

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        var destinations = request.IsBatch
            ? request
                .Destinations.Select(
                    (item, index) =>
                    {
                        var messageId = request.MessageId is null
                            ? null
                            : request.MessageId + (index + 1).ToString(CultureInfo.InvariantCulture);

                        return new SmsDestination(to: item.ToString(hasPlusPrefix: false), messageId: messageId);
                    }
                )
                .ToList()
            :
            [
                new SmsDestination(
                    to: request.Destinations[0].ToString(hasPlusPrefix: false),
                    messageId: request.MessageId
                ),
            ];

        var smsMessage = new SmsMessage(
            _options.Sender,
            destinations,
            new SmsMessageContent(new SmsTextContent(request.Text))
        );
        var smsRequest = new SmsRequest([smsMessage]);

        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var smsApi = new SmsApi(
            httpClient,
            new Configuration { BasePath = _options.BasePath, ApiKey = _options.ApiKey }
        );

        try
        {
            var smsResponse = await smsApi.SendSmsMessagesAsync(smsRequest, cancellationToken).ConfigureAwait(false);
            logger.LogTrace(
                "Infobip SMS sent successfully to {DestinationCount} recipients",
                request.Destinations.Count
            );

            return SendSingleSmsResponse.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException e)
        {
            logger.LogError(
                e,
                "Infobip SMS failed to {DestinationCount} recipients, ErrorCode={ErrorCode}",
                request.Destinations.Count,
                e.ErrorCode
            );
            FormattableString error = $"ErrorCode: {e.ErrorCode} {e.Message}";

            return SendSingleSmsResponse.Failed(error.ToInvariantString());
        }
    }
}
