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
        _uri = new(_options.SendSmsEndpointUrl);
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
            _logger.LogError("Empty response from VictoryLink API");

            return SendSingleSmsResponse.Failed("Empty response from VictoryLink API");
        }

        var isSuccess = rawContent.Contains("<Success>true</Success>", StringComparison.Ordinal);

        if (isSuccess)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        _logger.LogError("Failed to send VictoryLink - Message SMS={Message}", rawContent);

        return SendSingleSmsResponse.Failed("Failed to send.");
    }

    private string _BuildPayload(SendSingleSmsRequest request)
    {
        var hashableKey = _BuildHashableKey(request);
        var secureHash = _ComputeHash(hashableKey);

        // Simulate XML payload building.
        return "<Payload>" +
            $"<AccountId>{_options.AccountId}</AccountId>" +
            $"<Password>{_options.Password}</Password>" +
            $"<SenderName>{_options.Sender}</SenderName>" +
            $"<SecureHash>{secureHash}</SecureHash>" +
            // For multiple recipients use comma separated values.
            $"<Recipients>{request.Destination}</Recipients>" +
            $"<Message>{request.Text}</Message>" +
            "</Payload>";
    }

    private string _BuildHashableKey(SendSingleSmsRequest request)
    {
        var hashableKey = $"AccountId={_options.AccountId}&Password={_options.Password}";

        // For a single recipient
        hashableKey += $"&SenderName={_options.Sender}&ReceiverMSISDN={request.Destination}&SMSText={request.Text}";

        // For multiple recipients
        // foreach (var recipient in recipients)
        // {
        //     hashableKey += $"&SenderName={_options.Sender}&ReceiverMSISDN={recipient}&SMSText={request.Text}";
        // }

        return hashableKey;
    }

    private string _ComputeHash(string input)
    {
        using var hmac = new HMACSHA256(_secureHash);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        var hashString = BitConverter.ToString(hashBytes);

        return hashString.RemoveCharacter('-').ToUpper(CultureInfo.CurrentCulture);
    }
}
