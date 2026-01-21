// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

public class ConsumerExecutedResult(
    object? result,
    string msgId,
    string? callbackName,
    IDictionary<string, string?>? callbackHeader
)
{
    public object? Result { get; set; } = result;

    public string MessageId { get; set; } = msgId;

    public string? CallbackName { get; set; } = callbackName;

    public IDictionary<string, string?>? CallbackHeader { get; set; } = callbackHeader;
}
