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
) : ISmsSender, IBulkSmsSender
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
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        return await _SendAsync([request.Destination], request.Text, request.MessageId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        // VictoryLink accepts a comma-separated receiver list and returns a single response code, so the same
        // outcome applies to every recipient.
        var outcome = await _SendAsync(request.Destinations, request.Text, request.MessageId, cancellationToken)
            .ConfigureAwait(false);

        return SendBulkSmsResponse.FromAggregate(request.Destinations, outcome);
    }

    private async ValueTask<SendSingleSmsResponse> _SendAsync(
        IReadOnlyList<SmsRequestDestination> destinations,
        string text,
        string? messageId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _SendCoreAsync(destinations, text, messageId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, destinations.Count);

            return SendSingleSmsResponse.FromException(e);
        }
    }

    private async ValueTask<SendSingleSmsResponse> _SendCoreAsync(
        IReadOnlyList<SmsRequestDestination> destinations,
        string text,
        string? messageId,
        CancellationToken cancellationToken
    )
    {
        var victoryLinkRequest = new VictoryLinkRequest
        {
            SmsId = messageId ?? Guid.NewGuid().ToString(),
            UserName = _options.UserName,
            Password = _options.Password,
            SmsText = text,
            SmsLang = text.IsRtlText() ? "a" : "e",
            SmsSender = _options.Sender,
            SmsReceiver = string.Join(',', destinations.Select(x => x.ToString())),
        };

        using var httpClient = httpClientFactory.CreateClient(SetupVictoryLink.HttpClientName);
        using var response = await httpClient
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

        logger.LogFailedToSendSms(destinations.Count, responseMessage);

        return SendSingleSmsResponse.Failed(responseMessage, VictoryLinkResponseCodes.GetFailureKind(code));
    }
}
