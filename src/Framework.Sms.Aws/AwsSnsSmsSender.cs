using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Aws;

public sealed class AwsSnsSmsSender : ISmsSender
{
    private readonly IAmazonSimpleNotificationService _client;
    private readonly AwsSnsSmsSettings _settings;
    private readonly ILogger<AwsSnsSmsSender> _logger;

    public AwsSnsSmsSender(
        IAmazonSimpleNotificationService client,
        IOptions<AwsSnsSmsSettings> options,
        ILogger<AwsSnsSmsSender> logger
    )
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        var attributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
        {
            {
                "AWS.SNS.SMS.SenderID",
                new() { StringValue = _settings.SenderId, DataType = "String" }
            },
            {
                "AWS.SNS.SMS.MaxPrice",
                new() { StringValue = "0.50", DataType = "Number" }
            },
            {
                "AWS.SNS.SMS.SMSType",
                new() { StringValue = "Transactional", DataType = "String" }
            }
        };

        var publishRequest = new PublishRequest
        {
            PhoneNumber = request.Destination.ToString(),
            Message = request.Text,
            MessageAttributes = attributes
        };

        try
        {
            var publishResponse = await _client.PublishAsync(publishRequest, token);

            if (publishResponse.HttpStatusCode.IsSuccessStatusCode())
            {
                return SendSingleSmsResponse.Succeeded();
            }

            _logger.LogError("Failed to send SMS {@Request} {@Response}", publishRequest, publishResponse);

            return SendSingleSmsResponse.Failed(
                $"Failed to send SMS with status code {publishResponse.HttpStatusCode}"
            );
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to send SMS {@Request}", publishRequest);

            return SendSingleSmsResponse.Failed(e.Message);
        }
    }
}
