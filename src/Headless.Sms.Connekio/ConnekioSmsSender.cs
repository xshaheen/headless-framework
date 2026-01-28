// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Http;
using Headless.Sms.Connekio.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Connekio;

public sealed class ConnekioSmsSender(
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

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ConnekioSmsOptions _options = optionsAccessor.Value;
    private readonly ILogger<ConnekioSmsSender> _logger = logger;
    private readonly Uri _singleSmsEndpoint = new(optionsAccessor.Value.SingleSmsEndpoint);
    private readonly Uri _batchSmsEndpoint = new(optionsAccessor.Value.BatchSmsEndpoint);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _GetEndpoint(request.IsBatch));

        requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/json");

        requestMessage.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(
            $"{_options.UserName}:{_options.Password}:{_options.AccountId}"
        );

        using var httpClient = _httpClientFactory.CreateClient(ConnekioSetup.HttpClientName);
        var response = await httpClient.SendAsync(requestMessage, cancellationToken).AnyContext();
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).AnyContext();

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            _logger.LogError("Empty response from Connekio API");

            return SendSingleSmsResponse.Failed("Failed to send.");
        }

        if (response.IsSuccessStatusCode)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        _logger.LogError(
            "Failed to send SMS using Connekio API to {DestinationCount} recipients, StatusCode={StatusCode}",
            request.Destinations.Count,
            response.StatusCode
        );

        return SendSingleSmsResponse.Failed("Failed to send.");
    }

    #region Helpers

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
