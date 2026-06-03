// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class ConsumerExecutedResult(
    object? result,
    Type? resultType,
    string msgId,
    string? callbackName,
    IDictionary<string, string?>? callbackHeader,
    string? nextCallbackName = null
)
{
    public object? Result { get; init; } = result;

    public Type? ResultType { get; init; } = resultType;

    public string MessageId { get; init; } = msgId;

    /// <summary>The message name the captured response is published to (the current message's callback destination).</summary>
    public string? CallbackName { get; init; } = callbackName;

    public IDictionary<string, string?>? CallbackHeader { get; set; } = callbackHeader;

    /// <summary>
    /// The callback name to stamp on the published response so the next hop in a callback chain can
    /// react to it. Captured from <c>ConsumeContext.SetNextCallback</c>; maps to
    /// <c>PublishOptions.CallbackName</c> on the response publish.
    /// </summary>
    public string? NextCallbackName { get; init; } = nextCallbackName;
}
