// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Framework.Messages;

/// <summary>
/// Factory methods for creating AWS SNS and SQS clients with consistent configuration.
/// </summary>
internal static class AwsClientFactory
{
    /// <summary>
    /// Creates an SNS client with appropriate configuration based on options.
    /// </summary>
    /// <param name="options">AWS SQS options containing credentials and service URLs.</param>
    /// <returns>Configured SNS client.</returns>
    public static IAmazonSimpleNotificationService CreateSnsClient(AmazonSqsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SnsServiceUrl))
        {
            return options.Credentials != null
                ? new AmazonSimpleNotificationServiceClient(options.Credentials, options.Region)
                : new AmazonSimpleNotificationServiceClient(options.Region);
        }

        var config = new AmazonSimpleNotificationServiceConfig { ServiceURL = options.SnsServiceUrl };
        return options.Credentials != null
            ? new AmazonSimpleNotificationServiceClient(options.Credentials, config)
            : new AmazonSimpleNotificationServiceClient(config);
    }

    /// <summary>
    /// Creates an SQS client with appropriate configuration based on options.
    /// </summary>
    /// <param name="options">AWS SQS options containing credentials and service URLs.</param>
    /// <returns>Configured SQS client.</returns>
    public static IAmazonSQS CreateSqsClient(AmazonSqsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SqsServiceUrl))
        {
            return options.Credentials != null
                ? new AmazonSQSClient(options.Credentials, options.Region)
                : new AmazonSQSClient(options.Region);
        }

        var config = new AmazonSQSConfig { ServiceURL = options.SqsServiceUrl };
        return options.Credentials != null
            ? new AmazonSQSClient(options.Credentials, config)
            : new AmazonSQSClient(config);
    }
}
