// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Aws;

internal sealed class AwsSnsSmsSender(
    IAmazonSimpleNotificationService client,
    IOptions<AwsSnsSmsOptions> optionsAccessor,
    ILogger<AwsSnsSmsSender> logger
) : ISmsSender
{
    private readonly AwsSnsSmsOptions _options = optionsAccessor.Value;

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        if (request.Destinations.Count > 1)
        {
            return SendSingleSmsResponse.Failed(
                "AWS SNS does not support sending SMS to multiple destinations",
                SmsFailureKind.Unsupported
            );
        }

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
            PhoneNumber = request.Destinations[0].ToString(hasPlusPrefix: true),
            Message = request.Text,
            MessageAttributes = attributes,
        };

        try
        {
            var publishResponse = await client.PublishAsync(publishRequest, cancellationToken).ConfigureAwait(false);

            if (publishResponse.HttpStatusCode.IsSuccessStatusCode())
            {
                return SendSingleSmsResponse.Succeeded(publishResponse.MessageId);
            }

            logger.LogSmsSendFailed(request.Destinations.Count, publishResponse.HttpStatusCode);

            return SendSingleSmsResponse.Failed(
                $"Failed to send SMS using AWS with status code {publishResponse.HttpStatusCode}",
                SmsFailureKinds.FromHttpStatusCode(publishResponse.HttpStatusCode)
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, request.Destinations.Count);

            return SendSingleSmsResponse.FromException(e);
        }
    }
}

internal static partial class AwsSnsSmsSenderLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SmsSendFailed",
        Level = LogLevel.Error,
        Message = "Failed to send SMS to {DestinationCount} recipients, StatusCode={StatusCode}"
    )]
    public static partial void LogSmsSendFailed(this ILogger logger, int destinationCount, HttpStatusCode statusCode);

    [LoggerMessage(
        EventId = 2,
        EventName = "SmsSendException",
        Level = LogLevel.Error,
        Message = "Failed to send SMS using AWS to {DestinationCount} recipients"
    )]
    public static partial void LogSmsSendException(this ILogger logger, Exception exception, int destinationCount);
}
