// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Framework.Checks;
using Framework.Http;
using Framework.Sms.Connekio.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Connekio;

public sealed class ConnekioSmsSender : ISmsSender
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = ConnekioJsonSerializerContext.Default,
    };

    private readonly HttpClient _httpClient;
    private readonly ConnekioSmsOptions _options;
    private readonly ILogger<ConnekioSmsSender> _logger;
    private readonly Uri _singleSmsEndpoint;
    private readonly Uri _batchSmsEndpoint;

    public ConnekioSmsSender(
        HttpClient httpClient,
        IOptions<ConnekioSmsOptions> optionsAccessor,
        ILogger<ConnekioSmsSender> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = optionsAccessor.Value;
        _singleSmsEndpoint = new(_options.SingleSmsEndpoint);
        _batchSmsEndpoint = new(_options.BatchSmsEndpoint);
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _GetEndpoint(request.IsBatch));

        requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/json");

        requestMessage.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(
            $"{_options.UserName}:{_options.Password}:{_options.AccountId}"
        );

        var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            _logger.LogError("Empty response from Connekio API");

            return SendSingleSmsResponse.Failed("Failed to send.");
        }

        if (response.IsSuccessStatusCode)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        _logger.LogError("Failed to send SMS using Connekio API - Response={RawContent}", rawContent);

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
                MobileList = request.Destinations.ConvertAll(r => new ConnekioRecipient { Msisdn = r.ToString() }),
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
