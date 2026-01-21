// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AwsSqs;

internal class SqsReceivedMessage
{
    public string? Message { get; set; }

    public Dictionary<string, SqsReceivedMessageAttributes> MessageAttributes { get; set; } = default!;
}

internal class SqsReceivedMessageAttributes
{
    public string? Type { get; set; }

    public string? Value { get; set; }
}
