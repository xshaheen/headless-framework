using Infobip.Api.Client;
using Infobip.Api.Client.Api;
using Infobip.Api.Client.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Infobip;

public sealed class InfobipSmsSender : ISmsSender
{
    private readonly string _sender;
    private readonly Configuration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<InfobipSmsSender> _logger;

    public InfobipSmsSender(HttpClient httpClient, IOptions<InfobipSettings> options, ILogger<InfobipSmsSender> logger)
    {
        var value = options.Value;
        _sender = value.Sender;

        _configuration = new()
        {
            BasePath = value.BasePath,
            ApiKeyPrefix = value.ApiKeyPrefix,
            ApiKey = value.ApiKey,
        };

        _httpClient = httpClient;
        _logger = logger;
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        FormattableString to = $"{request.Destination.Code}{request.Destination.Number}";

        var smsMessage = new SmsTextualMessage
        {
            From = _sender,
            Destinations = [new(messageId: Guid.NewGuid().ToString(), to: to.ToInvariantString())],
            Text = request.Text,
        };

        var smsRequest = new SmsAdvancedTextualRequest { Messages = [smsMessage] };
        var sendSmsApi = new SendSmsApi(_httpClient, _configuration);

        try
        {
            var smsResponse = await sendSmsApi.SendSmsMessageAsync(smsRequest, token);
            _logger.LogInformation("Infobip SMS request {@Request} success {@Response}", smsRequest, smsResponse);

            return SendSingleSmsResponse.Succeeded();
        }
        catch (ApiException e)
        {
            _logger.LogError(e, "Infobip SMS request {@Request} failed {@Error}", smsRequest, e.ErrorContent);
            FormattableString error = $"ErrorCode: {e.ErrorCode} {e.ErrorContent ?? e.Message}";

            return SendSingleSmsResponse.Failed(error.ToInvariantString());
        }
    }
}
