// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Http;
using Headless.Sms.Connekio.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Connekio;

internal sealed class ConnekioSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnekioSmsOptions> optionsAccessor,
    ILogger<ConnekioSmsSender> logger
) : ISmsSender
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = ConnekioJsonSerializerContext.Default,
    };

    private readonly ConnekioSmsOptions _options = optionsAccessor.Value;
    private readonly Uri _singleSmsEndpoint = new(optionsAccessor.Value.SingleSmsEndpoint);
    private readonly Uri _batchSmsEndpoint = new(optionsAccessor.Value.BatchSmsEndpoint);

    // Credentials are fixed at construction, so build the (immutable) Basic auth header once instead of
    // re-interpolating + base64-encoding it on every send.
    private readonly AuthenticationHeaderValue _basicAuthHeader = AuthenticationHeaderFactory.CreateBasic(
        $"{optionsAccessor.Value.UserName}:{optionsAccessor.Value.Password}:{optionsAccessor.Value.AccountId}"
    );

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

    #region Helpers

    private async ValueTask<SendSingleSmsResponse> _SendCoreAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken
    )
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _GetEndpoint(request.IsBatch));

        requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/json");

        requestMessage.Headers.Authorization = _basicAuthHeader;

        using var httpClient = httpClientFactory.CreateClient(SetupConnekio.HttpClientName);
        var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);

        // A success status code is authoritative; only read the body to explain a failure.
        if (response.IsSuccessStatusCode)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            logger.LogEmptyResponse();
        }
        else
        {
            logger.LogFailedToSendSms(request.Destinations.Count, response.StatusCode);
        }

        return SendSingleSmsResponse.Failed("Failed to send.");
    }

    private string _BuildPayload(SendSingleSmsRequest request)
    {
        if (request.IsBatch)
        {
            var batchRequest = new ConnekioBatchSmsRequest
            {
                AccountId = _options.AccountId,
                Sender = _options.Sender,
                Text = request.Text,
                MobileList = request.Destinations.Select(r => new ConnekioRecipient { Msisdn = r.ToString() }).ToList(),
            };

            return JsonSerializer.Serialize(batchRequest, _JsonOptions);
        }

        var singleRequest = new ConnekioSingleSmsRequest
        {
            AccountId = _options.AccountId,
            Sender = _options.Sender,
            Text = request.Text,
            Msisdn = request.Destinations[0].ToString(),
        };

        return JsonSerializer.Serialize(singleRequest, _JsonOptions);
    }

    private Uri _GetEndpoint(bool isBatch)
    {
        return isBatch ? _batchSmsEndpoint : _singleSmsEndpoint;
    }

    #endregion
}
