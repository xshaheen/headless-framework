// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Checks;
using Framework.Sms.VictoryLink.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.VictoryLink;

public sealed class VictoryLinkSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<VictoryLinkSmsOptions> optionsAccessor,
    ILogger<VictoryLinkSmsSender> logger
) : ISmsSender
{
    public const string HttpClientName = "VictoryLinkSms";

    private readonly VictoryLinkSmsOptions _options = optionsAccessor.Value;
    private readonly Uri _uri = new(optionsAccessor.Value.Endpoint);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
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
            SmsReceiver = request.IsBatch
                ? string.Join(',', request.Destinations.Select(x => x.Number))
                : request.Destinations[0].Number,
        };

        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.PostAsJsonAsync(_uri, victoryLinkRequest, cancellationToken);
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            logger.LogError("Empty response from VictoryLink SMS API");

            return SendSingleSmsResponse.Failed("Failed to send.");
        }

        if (VictoryLinkResponseCodes.IsSuccess(rawContent))
        {
            return SendSingleSmsResponse.Succeeded();
        }

        var responseMessage = VictoryLinkResponseCodes.GetCodeMeaning(rawContent);

        logger.LogError(
            "Failed to send SMS using VictoryLink. ResponseContent={Content} - ResponseContent={RawContent}",
            responseMessage,
            rawContent
        );

        return SendSingleSmsResponse.Failed(responseMessage);
    }
}
