// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Checks;
using Framework.Sms.VictoryLink.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.VictoryLink;

public sealed class VictoryLinkSmsSender(
    HttpClient httpClient,
    IOptions<VictoryLinkOptions> optionsAccessor,
    ILogger<VictoryLinkSmsSender> logger
) : ISmsSender
{
    private readonly VictoryLinkOptions _options = optionsAccessor.Value;
    private readonly Uri _uri = new(optionsAccessor.Value.SendSmsEndpointUrl);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(request);

        var victoryLinkRequest = new VictoryLinkRequest
        {
            SmsId = request.MessageId ?? Guid.NewGuid().ToString(),
            UserName = _options.UserName,
            Password = _options.Password,
            SmsText = request.Text,
            SmsLang = request.Text.IsRtlText() ? "a" : "e",
            SmsSender = _options.Sender,
            SmsReceiver = request.Destination.Number,
        };

        var response = await httpClient.PostAsJsonAsync(_uri, victoryLinkRequest, token);
        var responseContent = await response.Content.ReadAsStringAsync(token);

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            logger.LogError("Empty response from VictoryLink API");

            return SendSingleSmsResponse.Failed("Empty response from VictoryLink API");
        }

        if (VictoryLinkResponseCodes.IsSuccess(responseContent))
        {
            return SendSingleSmsResponse.Succeeded();
        }

        var responseMessage = VictoryLinkResponseCodes.GetCodeMeaning(responseContent);

        logger.LogError(
            "Failed to send VictoryLink - ResponseContent={Content} - Message SMS={Message}",
            responseMessage,
            responseContent
        );

        return SendSingleSmsResponse.Failed(responseMessage);
    }
}
