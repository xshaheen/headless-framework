// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

public class ConsumerExecutedResult(
    object? result,
    Type? resultType,
    string msgId,
    string? callbackName,
    IDictionary<string, string?>? callbackHeader
)
{
    public object? Result { get; set; } = result;

    public Type? ResultType { get; set; } = resultType;

    public string MessageId { get; set; } = msgId;

    public string? CallbackName { get; set; } = callbackName;

    public IDictionary<string, string?>? CallbackHeader { get; set; } = callbackHeader;
}
