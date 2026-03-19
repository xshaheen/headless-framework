// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
}
