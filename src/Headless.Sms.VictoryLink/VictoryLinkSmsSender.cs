// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Sms.VictoryLink.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.VictoryLink;

public sealed class VictoryLinkSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<VictoryLinkSmsOptions> optionsAccessor,
    ILogger<VictoryLinkSmsSender> logger
) : ISmsSender
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = VictoryLinkJsonSerializerContext.Default,
    };

    private readonly VictoryLinkSmsOptions _options = optionsAccessor.Value;
    private readonly Uri _uri = new(optionsAccessor.Value.Endpoint);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

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

        using var httpClient = httpClientFactory.CreateClient(VictoryLinkSetup.HttpClientName);
        var response = await httpClient
            .PostAsJsonAsync(_uri, victoryLinkRequest, _JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            logger.LogEmptyResponse();

            return SendSingleSmsResponse.Failed("Failed to send.");
        }

        if (VictoryLinkResponseCodes.IsSuccess(rawContent))
        {
            return SendSingleSmsResponse.Succeeded();
        }

        var responseMessage = VictoryLinkResponseCodes.GetCodeMeaning(rawContent);

        logger.LogFailedToSendSms(request.Destinations.Count, responseMessage);

        return SendSingleSmsResponse.Failed(responseMessage);
    }
}
