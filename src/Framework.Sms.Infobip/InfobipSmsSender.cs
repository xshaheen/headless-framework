// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Infobip.Api.Client;
using Infobip.Api.Client.Api;
using Infobip.Api.Client.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Infobip;

public sealed class InfobipSmsSender : ISmsSender
{
    private readonly string _sender;
    private readonly SmsApi _smsApi;
    private readonly ILogger<InfobipSmsSender> _logger;

    public InfobipSmsSender(
        HttpClient httpClient,
        IOptions<InfobipOptions> optionsAccessor,
        ILogger<InfobipSmsSender> logger
    )
    {
        var value = optionsAccessor.Value;
        _sender = value.Sender;
        _smsApi = new SmsApi(httpClient, new Configuration { BasePath = value.BasePath, ApiKey = value.ApiKey });
        _logger = logger;
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        var destination = request.IsBatch
            ? request
                .Destinations.Select(
                    (item, index) =>
                    {
                        var messageId = request.MessageId is null
                            ? null
                            : request.MessageId + (index + 1).ToString(CultureInfo.InvariantCulture);

                        return new SmsDestination(messageId, to: item.ToString(hasPlusPrefix: false));
                    }
                )
                .ToList()
            : [new SmsDestination(request.MessageId, to: request.Destinations[0].ToString(hasPlusPrefix: false))];

        var smsMessage = new SmsTextualMessage
        {
            From = _sender,
            Text = request.Text,
            Destinations = destination,
        };

        var smsRequest = new SmsAdvancedTextualRequest { Messages = [smsMessage] };

        try
        {
            var smsResponse = await _smsApi.SendSmsMessageAsync(smsRequest, token);
            _logger.LogTrace("Infobip SMS request {@Request} success {@Response}", smsRequest, smsResponse);

            return SendSingleSmsResponse.Succeeded();
        }
        catch (ApiException e)
        {
            _logger.LogError(e, "Infobip SMS request {@Request} failed {@Error}", smsRequest, e.ErrorContent);
            FormattableString error = $"ErrorCode: {e.ErrorCode} {e.ErrorContent ?? e.Message}";

            return SendSingleSmsResponse.Failed(error.ToInvariantString());
        }
    }
}
