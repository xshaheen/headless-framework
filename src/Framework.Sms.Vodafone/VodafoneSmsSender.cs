// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Framework.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Vodafone;

public sealed class VodafoneSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly VodafoneSmsOptions _options;
    private readonly ILogger<VodafoneSmsSender> _logger;
    private readonly Uri _uri;
    private readonly byte[] _secureHash;

    public VodafoneSmsSender(
        HttpClient httpClient,
        IOptions<VodafoneSmsOptions> optionsAccessor,
        ILogger<VodafoneSmsSender> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = optionsAccessor.Value;
        _uri = new(_options.SendSmsEndpoint);
        _secureHash = Encoding.UTF8.GetBytes(_options.SecureHash);
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(request);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _uri);
        requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/xml");

        var response = await _httpClient.PostAsJsonAsync(_uri, requestMessage, token);
        var rawContent = await response.Content.ReadAsStringAsync(token);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            _logger.LogError("Empty response from Vodafone API");

            return SendSingleSmsResponse.Failed("Failed to send.");
        }

        var isSuccess = rawContent.Contains("<Success>true</Success>", StringComparison.Ordinal);

        if (isSuccess)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        _logger.LogError("Failed to send SMS using Vodafone API. Response={RawContent}", rawContent);

        return SendSingleSmsResponse.Failed("Failed to send.");
    }

    private string _BuildPayload(SendSingleSmsRequest request)
    {
        var hashableKey = _BuildHashableKey(request);
        var secureHash = _ComputeHash(hashableKey);
        var recipients = request.IsBatch ? string.Join(',', request.Destinations) : request.Destinations[0].ToString();

        // Simulate XML payload building.
        return "<Payload>"
            + $"<AccountId>{_options.AccountId}</AccountId>"
            + $"<Password>{_options.Password}</Password>"
            + $"<SenderName>{_options.Sender}</SenderName>"
            + $"<SecureHash>{secureHash}</SecureHash>"
            + $"<Recipients>{recipients}</Recipients>"
            + $"<Message>{request.Text}</Message>"
            + "</Payload>";
    }

    private string _BuildHashableKey(SendSingleSmsRequest request)
    {
        var hashableKey = new StringBuilder();

        hashableKey.Append(
            CultureInfo.InvariantCulture,
            $"AccountId={_options.AccountId}&Password={_options.Password}"
        );

        if (request.IsBatch)
        {
            foreach (var recipient in request.Destinations)
            {
                hashableKey.Append(
                    CultureInfo.InvariantCulture,
                    $"&SenderName={_options.Sender}&ReceiverMSISDN={recipient}&SMSText={request.Text}"
                );
            }
        }
        else
        {
            hashableKey.Append(
                CultureInfo.InvariantCulture,
                $"&SenderName={_options.Sender}&ReceiverMSISDN={request.Destinations[0]}&SMSText={request.Text}"
            );
        }

        return hashableKey.ToString();
    }

    private string _ComputeHash(string input)
    {
        using var hmac = new HMACSHA256(_secureHash);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        var hashString = BitConverter.ToString(hashBytes);

        return hashString.RemoveCharacter('-').ToUpper(CultureInfo.CurrentCulture);
    }
}
