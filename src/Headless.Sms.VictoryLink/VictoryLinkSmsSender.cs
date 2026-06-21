// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Sms.VictoryLink.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.VictoryLink;

internal sealed class VictoryLinkSmsSender(
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

        try
        {
            return await _SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, request.Destinations.Count);

            return SendSingleSmsResponse.Failed(e.Message, SmsFailureKind.Transient);
        }
    }

    private async ValueTask<SendSingleSmsResponse> _SendCoreAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken
    )
    {
        var victoryLinkRequest = new VictoryLinkRequest
        {
            SmsId = request.MessageId ?? Guid.NewGuid().ToString(),
            UserName = _options.UserName,
            Password = _options.Password,
            SmsText = request.Text,
            SmsLang = request.Text.IsRtlText() ? "a" : "e",
            SmsSender = _options.Sender,
            SmsReceiver = request.IsBatch
                ? string.Join(',', request.Destinations.Select(x => x.ToString()))
                : request.Destinations[0].ToString(),
        };

        using var httpClient = httpClientFactory.CreateClient(SetupVictoryLink.HttpClientName);
        var response = await httpClient
            .PostAsJsonAsync(_uri, victoryLinkRequest, _JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            logger.LogEmptyResponse();

            return SendSingleSmsResponse.Failed("Failed to send.");
        }

        // The API returns the numeric code as a bare/JSON-quoted string; normalize before matching.
        var code = rawContent.Trim().Trim('"').Trim();

        if (VictoryLinkResponseCodes.IsSuccess(code))
        {
            return SendSingleSmsResponse.Succeeded();
        }

        var responseMessage = VictoryLinkResponseCodes.GetCodeMeaning(code);

        logger.LogFailedToSendSms(request.Destinations.Count, responseMessage);

        return SendSingleSmsResponse.Failed(responseMessage, VictoryLinkResponseCodes.GetFailureKind(code));
    }
}
