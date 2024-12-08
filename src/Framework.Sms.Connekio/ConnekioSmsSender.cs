// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Framework.BuildingBlocks.Helpers.Network;
using Framework.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Connekio;

public sealed class ConnekioSmsSender : ISmsSender
{
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
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(request);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _GetEndpoint(request.IsBatch));

        requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/json");

        requestMessage.Headers.Authorization = AuthenticationHeaderValueFactory.CreateBasic(
            $"{_options.UserName}:{_options.Password}:{_options.AccountId}"
        );

        var response = await _httpClient.SendAsync(requestMessage, token);
        var rawContent = await response.Content.ReadAsStringAsync(token);

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
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "account_id", _options.AccountId },
            { "sender", _options.Sender },
            { "text", request.Text },
        };

        if (request.IsBatch)
        {
            payload["mobile_list"] = request.Destinations.ConvertAll(recipient =>
            {
                var obj = new Dictionary<string, string>(StringComparer.Ordinal) { ["msisdn"] = recipient.ToString() };

                return obj;
            });
        }
        else
        {
            payload["msisdn"] = request.Destination.ToString();
        }

        return JsonSerializer.Serialize(payload);
    }

    private Uri _GetEndpoint(bool isBatch)
    {
        return isBatch ? _batchSmsEndpoint : _singleSmsEndpoint;
    }

    #endregion
}
