// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Aws;

public sealed class AwsSnsSmsSender(
    IAmazonSimpleNotificationService client,
    IOptions<AwsSnsSmsOptions> optionsAccessor,
    ILogger<AwsSnsSmsSender> logger
) : ISmsSender
{
    private readonly AwsSnsSmsOptions _options = optionsAccessor.Value;

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        var attributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
        {
            {
                "AWS.SNS.SMS.SenderID",
                new() { StringValue = _options.SenderId, DataType = "String" }
            },
            {
                "AWS.SNS.SMS.SMSType",
                new() { StringValue = "Transactional", DataType = "String" }
            },
        };

        if (_options.MaxPrice.HasValue)
        {
            attributes.Add(
                "AWS.SNS.SMS.MaxPrice",
                new()
                {
                    StringValue = _options.MaxPrice.Value.ToString(CultureInfo.InvariantCulture),
                    DataType = "Number",
                }
            );
        }

        var publishRequest = new PublishRequest
        {
            PhoneNumber = request.Destination.ToString(),
            Message = request.Text,
            MessageAttributes = attributes,
        };

        try
        {
            var publishResponse = await client.PublishAsync(publishRequest, token);

            if (publishResponse.HttpStatusCode.IsSuccessStatusCode())
            {
                return SendSingleSmsResponse.Succeeded();
            }

            logger.LogError("Failed to send SMS {@Request} {@Response}", publishRequest, publishResponse);

            return SendSingleSmsResponse.Failed(
                $"Failed to send SMS using AWS with status code {publishResponse.HttpStatusCode}"
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to send using AWS SMS {@Request}", publishRequest);

            return SendSingleSmsResponse.Failed(e.Message);
        }
    }
}
