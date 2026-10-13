// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;

namespace Headless.Messaging.RequestReply;

/// <summary>An ok reply as the waiting call receives it: the response and what the caller may read of the headers.</summary>
internal sealed record ReceivedReply(
    object Message,
    IReadOnlyDictionary<string, string?> Headers,
    string? MessageId,
    string? CorrelationId
)
{
    /// <summary>
    /// Builds the reply from the wire headers, keeping only those the responder set: the framework's own and trace
    /// propagation headers are not the responder's to send, so a forged one never reaches the caller.
    /// </summary>
    public static ReceivedReply From(object message, IDictionary<string, string?> wireHeaders)
    {
        wireHeaders.TryGetValue(Messaging.Headers.MessageId, out var messageId);
        wireHeaders.TryGetValue(Messaging.Headers.CorrelationId, out var correlationId);

        Dictionary<string, string?>? responderHeaders = null;
        foreach (var (name, value) in wireHeaders)
        {
            if (!Messaging.Headers.IsReservedOnReply(name))
            {
                (responderHeaders ??= new(StringComparer.Ordinal))[name] = value;
            }
        }

        return new ReceivedReply(
            message,
            responderHeaders?.ToFrozenDictionary(StringComparer.Ordinal) ?? FrozenDictionary<string, string?>.Empty,
            messageId,
            correlationId
        );
    }
}
