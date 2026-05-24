// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS.Model;

namespace Headless.Messaging.Aws;

internal static class TopicNormalizer
{
    public static string NormalizeForAws(this string origin)
    {
        return _NormalizeForAws(origin, maxLength: 256, "AWS SNS topic names must be 256 characters or less");
    }

    public static string NormalizeForSqsQueueName(this string origin)
    {
        return _NormalizeForAws(origin, maxLength: 80, "AWS SQS queue names must be 80 characters or less");
    }

    private static string _NormalizeForAws(string origin, int maxLength, string maxLengthMessage)
    {
        Argument.IsNotNullOrWhiteSpace(origin);

        const string fifoSuffix = ".fifo";
        string normalized;

        if (origin.EndsWith(fifoSuffix, StringComparison.Ordinal))
        {
            var name = origin[..^fifoSuffix.Length].Replace('.', '-').Replace(':', '_');
            normalized = name + fifoSuffix;
        }
        else
        {
            normalized = origin.Replace('.', '-').Replace(':', '_');
        }

        Argument.IsLessThanOrEqualTo(normalized.Length, maxLength, maxLengthMessage);

        return normalized;
    }

    public static bool IsAwsFifoName(this string name)
    {
        return name.EndsWith(".fifo", StringComparison.Ordinal);
    }

    public static CreateQueueRequest ToSqsCreateQueueRequest(this string origin)
    {
        var queueName = origin.NormalizeForSqsQueueName();
        var request = new CreateQueueRequest
        {
            QueueName = queueName,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        if (queueName.IsAwsFifoName())
        {
            request.Attributes["FifoQueue"] = "true";
            request.Attributes["ContentBasedDeduplication"] = "true";
        }

        return request;
    }

    public static CreateTopicRequest ToSnsCreateTopicRequest(this string origin)
    {
        var topicName = origin.NormalizeForAws();
        var request = new CreateTopicRequest(topicName)
        {
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        if (topicName.IsAwsFifoName())
        {
            request.Attributes["FifoTopic"] = "true";
            request.Attributes["ContentBasedDeduplication"] = "true";
        }

        return request;
    }
}
