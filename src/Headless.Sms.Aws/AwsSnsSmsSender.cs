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
    IOptionsMonitor<AwsSnsSmsOptions> optionsMonitor,
    string? optionsName,
    ILogger<AwsSnsSmsSender> logger
) : ISmsSender
{
    // Snapshot for this instance's options name — never CurrentValue, which binds the default options and
    // would bleed configuration across keyed instances.
    private readonly AwsSnsSmsOptions _options = optionsMonitor.Get(optionsName);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

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
            PhoneNumber = request.Destination.ToString(hasPlusPrefix: true),
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

            logger.LogSmsSendFailed(destinationCount: 1, publishResponse.HttpStatusCode);

            // SNS signals errors as typed exceptions; a non-success status on a non-throwing response has no
            // documented meaning, so it is not classified.
            return SendSingleSmsResponse.Failed(
                $"Failed to send SMS using AWS with status code {publishResponse.HttpStatusCode}",
                SmsFailureKind.Unknown
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, destinationCount: 1);

            // Classify from the SNS SDK's typed exception contract; anything unlisted falls back to the
            // shared transport classifier.
            var kind = e switch
            {
                AuthorizationErrorException or InvalidSecurityException => SmsFailureKind.AuthFailure,
                ThrottledException => SmsFailureKind.RateLimited,
                OptedOutException => SmsFailureKind.InvalidRecipient,
                InternalErrorException => SmsFailureKind.Transient,
                _ => SmsFailureKinds.FromException(e),
            };

            return SendSingleSmsResponse.FromException(e, kind);
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
