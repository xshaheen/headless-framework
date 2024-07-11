using System.Net.Http.Json;
using Framework.Sms.VictoryLink.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.VictoryLink;

public sealed class VictoryLinkSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VictoryLinkSmsSender> _logger;
    private readonly VictoryLinkSettings _settings;

    private readonly Uri _uri =
        new("https://smsvas.vlserv.com/VLSMSPlatformResellerAPI/NewSendingAPI/api/SMSSender/SendSMS");

    private readonly Dictionary<string, string> _responseCodeMap =
        new(StringComparer.Ordinal)
        {
            { "0", "Message Sent Successfully" },
            { "-1", "User is not subscribed" },
            { "-5", "Out of credit." },
            { "-10", "Queued Message, no need to send it again." },
            { "-11", "Invalid language." },
            { "-12", "SMS is empty." },
            { "-13", "Invalid fake sender exceeded 12 chars or empty." },
            { "-25", "Sending rate greater than receiving rate (only for send/receive accounts)." },
            { "-100", "Other error" },
        };

    public VictoryLinkSmsSender(
        HttpClient httpClient,
        IOptions<VictoryLinkSettings> options,
        ILogger<VictoryLinkSmsSender> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = options.Value;
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        var victoryLinkRequest = new VictoryLinkRequest
        {
            UserName = _settings.UserName,
            Password = _settings.Password,
            SmsText = request.Text,
            SmsLang = request.Text.IsRtlText() ? "a" : "e",
            SmsSender = _settings.Sender,
            SmsReceiver = request.Destination.Number,
            SmsId = request.MessageId ?? Guid.NewGuid().ToString()
        };

        var response = await _httpClient.PostAsJsonAsync(_uri, victoryLinkRequest, token);
        var content = await response.Content.ReadAsStringAsync(token);

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogError("Empty response from VictoryLink API");

            return SendSingleSmsResponse.Failed("Empty response from VictoryLink API");
        }

        if (!_responseCodeMap.TryGetValue(content, out var message))
        {
            _logger.LogError("Unknown response code from VictoryLink API: {ResponseCode}", content);

            return SendSingleSmsResponse.Failed("Unknown response code from VictoryLink API");
        }

        if (string.Equals(content, "0", StringComparison.Ordinal))
        {
            return SendSingleSmsResponse.Succeeded();
        }

        var codeMessage = _responseCodeMap.GetOrDefault(content) ?? content;

        _logger.LogError(
            "Failed to send VictoryLink SMS={Message} ResponseContent={Content} CodeMessage={Code}",
            message,
            content,
            codeMessage
        );

        return SendSingleSmsResponse.Failed(message);
    }
}
