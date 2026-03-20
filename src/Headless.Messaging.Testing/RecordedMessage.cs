// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MsgHeaders = Headless.Messaging.Headers;

namespace Headless.Messaging.Testing;

/// <summary>Identifies which observable collection a recorded message belongs to.</summary>
public enum MessageObservationType
{
    /// <summary>Message was published.</summary>
    Published,

    /// <summary>Message was consumed successfully.</summary>
    Consumed,

    /// <summary>Message processing faulted.</summary>
    Faulted,
}

/// <summary>A message captured by the test harness.</summary>
public sealed record RecordedMessage
{
    /// <summary>The CLR type of the message payload.</summary>
    public required Type MessageType { get; init; }

    /// <summary>The deserialized message payload.</summary>
    public required object Message { get; init; }

    /// <summary>Unique message identifier.</summary>
    public required string MessageId { get; init; }

    /// <summary>Optional correlation identifier.</summary>
    public required string? CorrelationId { get; init; }

    /// <summary>Message headers.</summary>
    public required IReadOnlyDictionary<string, string?> Headers { get; init; }

    /// <summary>The topic the message was published to or consumed from.</summary>
    public required string Topic { get; init; }

    /// <summary>UTC timestamp when the message was observed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The exception, if any (set for Faulted messages only).</summary>
    public Exception? Exception { get; init; }

    /// <summary>Creates a <see cref="RecordedMessage"/> by extracting standard header values.</summary>
    internal static RecordedMessage FromHeaders(
        IDictionary<string, string?> headers,
        object message,
        Type messageType,
        Exception? exception = null
    )
    {
        var messageId = headers.TryGetValue(MsgHeaders.MessageId, out var id) ? id ?? string.Empty : string.Empty;
        var correlationId =
            headers.TryGetValue(MsgHeaders.CorrelationId, out var corrId) && !string.IsNullOrWhiteSpace(corrId)
                ? corrId
                : null;
        var topic = headers.TryGetValue(MsgHeaders.MessageName, out var name) ? name ?? string.Empty : string.Empty;

        return new RecordedMessage
        {
            MessageType = messageType,
            Message = message,
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = new Dictionary<string, string?>(headers, StringComparer.Ordinal),
            Topic = topic,
            Timestamp = DateTimeOffset.UtcNow,
            Exception = exception,
        };
    }
}
