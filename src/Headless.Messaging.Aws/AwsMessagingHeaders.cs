// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Aws;

/// <summary>Framework-defined header names used with the Amazon SQS/SNS transport.</summary>
[PublicAPI]
public static class AwsMessagingHeaders
{
    /// <summary>
    /// Header carrying the SQS FIFO <c>MessageGroupId</c>. Messages that share the same group
    /// identifier are delivered in order within that group. Only meaningful for FIFO queues;
    /// setting this on a standard queue has no effect.
    /// </summary>
    public const string MessageGroupId = "headless-aws-message-group-id";
}
