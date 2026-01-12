// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Connekio;

public sealed class ConnekioSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnekioSmsOptions> optionsAccessor,
    ILogger<ConnekioSmsSender> logger
) : ISmsSender
{
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

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _GetEndpoint(request.IsBatch));

        requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/json");

        requestMessage.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(
            $"{_options.UserName}:{_options.Password}:{_options.AccountId}"
        );

        using var httpClient = _httpClientFactory.CreateClient("ConnekioSms");
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
            payload["msisdn"] = request.Destinations[0].ToString();
        }

        return JsonSerializer.Serialize(payload);
    }

    private Uri _GetEndpoint(bool isBatch)
    {
        return isBatch ? _batchSmsEndpoint : _singleSmsEndpoint;
    }

    #endregion
}
