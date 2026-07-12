// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MsgHeaders = Headless.Messaging.Headers;

namespace Headless.Messaging.Testing;

/// <summary>Identifies which observable collection a recorded message belongs to.</summary>
/// <remarks>
/// Additional members may be added in future versions, so consumers that switch on this enum should
/// include a default branch to handle values they do not recognize.
/// </remarks>
[PublicAPI]
public enum MessageObservationType
{
    /// <summary>Message was published.</summary>
    Published = 0,

    /// <summary>Message was consumed successfully.</summary>
    Consumed = 1,

    /// <summary>Message processing faulted.</summary>
    Faulted = 2,

    /// <summary>
    /// Retry budget was exhausted and the framework invoked <c>RetryPolicy.OnExhausted</c>.
    /// Recorded BEFORE the user-supplied callback runs, so a hanging or throwing callback
    /// cannot lose the observation.
    /// </summary>
    Exhausted = 3,
}

/// <summary>A message captured by the test harness.</summary>
[PublicAPI]
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

    /// <summary>The message name the message was published to or consumed from.</summary>
    public required string MessageName { get; init; }

    /// <summary>The bus/queue intent that produced the observation.</summary>
    public required IntentType IntentType { get; init; }

    /// <summary>
    /// UTC wall-clock time when the observation was recorded — publish acknowledgment
    /// or consume completion, not the original message creation time.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The exception, if any (set for Faulted messages only).</summary>
    public Exception? Exception { get; init; }

    /// <summary>Creates a <see cref="RecordedMessage"/> by extracting standard header values.</summary>
    internal static RecordedMessage FromHeaders(
        IDictionary<string, string?> headers,
        object message,
        Type messageType,
        DateTimeOffset timestamp,
        IntentType intentType = IntentType.Bus,
        Exception? exception = null
    )
    {
        var messageId = headers.TryGetValue(MsgHeaders.MessageId, out var id) ? id ?? string.Empty : string.Empty;
        var correlationId =
            headers.TryGetValue(MsgHeaders.CorrelationId, out var corrId) && !string.IsNullOrWhiteSpace(corrId)
                ? corrId
                : null;
        var messageName = headers.TryGetValue(MsgHeaders.MessageName, out var name)
            ? name ?? string.Empty
            : string.Empty;

        return new RecordedMessage
        {
            MessageType = messageType,
            Message = message,
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = new Dictionary<string, string?>(headers, StringComparer.Ordinal),
            MessageName = messageName,
            IntentType = intentType,
            Timestamp = timestamp,
            Exception = exception,
        };
    }
}
