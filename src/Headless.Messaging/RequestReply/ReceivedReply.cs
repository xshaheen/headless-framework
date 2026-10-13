// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;

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
    /// propagation headers are not the responder's to send, so a forged one never reaches the caller, and a header
    /// without a value is dropped so every transport gives the caller the same set.
    /// </summary>
    public static ReceivedReply From(object message, IDictionary<string, string?> wireHeaders)
    {
        wireHeaders.TryGetValue(Messaging.Headers.MessageId, out var messageId);
        wireHeaders.TryGetValue(Messaging.Headers.CorrelationId, out var correlationId);

        Dictionary<string, string?>? responderHeaders = null;
        foreach (var (name, value) in wireHeaders)
        {
            // A null value is skipped too: some transports drop it on the wire and others deliver it, so a caller never
            // sees one on any of them.
            if (value is not null && !Messaging.Headers.IsReservedOnReply(name))
            {
                (responderHeaders ??= new(StringComparer.Ordinal))[name] = value;
            }
        }

        return new ReceivedReply(
            message,
            // A read-only view rather than a frozen copy: the map is small, built once per reply, and read rarely.
            responderHeaders is null
                ? ReadOnlyDictionary<string, string?>.Empty
                : new ReadOnlyDictionary<string, string?>(responderHeaders),
            messageId,
            correlationId
        );
    }
}
