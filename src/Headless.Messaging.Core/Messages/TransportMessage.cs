// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Framework.Checks;

namespace Headless.Messaging.Messages;

/// <summary>
/// Represents a message in transit between the message broker and application.
/// This struct encapsulates the message headers (metadata) and body (serialized content) as received from or sent to a broker.
/// </summary>
/// <remarks>
/// This is a value type optimized for performance when passing messages through the message processing pipeline.
/// Unlike <see cref="Message"/>, this struct works with raw byte data rather than deserialized objects,
/// making it suitable for transport-level operations.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="TransportMessage"/> struct with the specified headers and body.
/// </remarks>
/// <param name="headers">
/// A dictionary of message metadata headers (MessageId, MessageName, Group, etc.).
/// </param>
/// <param name="body">
/// The raw message body as bytes. This is typically a UTF-8 encoded JSON string.
/// </param>
/// <exception cref="ArgumentNullException">Thrown if <paramref name="headers"/> is null.</exception>
[StructLayout(LayoutKind.Auto)]
public readonly struct TransportMessage(IDictionary<string, string?> headers, ReadOnlyMemory<byte> body)
    : IEquatable<TransportMessage>
{
    /// <summary>
    /// Gets the metadata headers of this message.
    /// Headers contain system information such as message ID, name, group, and custom application data.
    /// </summary>
    public IDictionary<string, string?> Headers { get; } = Argument.IsNotNull(headers);

    /// <summary>
    /// Gets the raw message body as a read-only byte buffer.
    /// This typically contains UTF-8 encoded JSON or other serialized content.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; } = body;

    /// <summary>
    /// Retrieves the unique message identifier from the message headers.
    /// </summary>
    /// <returns>The message ID stored in the <see cref="Headless.Messaging.Messages.Headers.MessageId"/> header.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the MessageId header is not present.</exception>
    public string GetId()
    {
        return Headers[Headless.Messaging.Messages.Headers.MessageId]!;
    }

    /// <summary>
    /// Retrieves the message name or topic from the message headers.
    /// </summary>
    /// <returns>The message name stored in the <see cref="Headless.Messaging.Messages.Headers.MessageName"/> header.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the MessageName header is not present.</exception>
    public string GetName()
    {
        return Headers[Headless.Messaging.Messages.Headers.MessageName]!;
    }

    /// <summary>
    /// Attempts to retrieve the consumer group name from the message headers.
    /// </summary>
    /// <returns>
    /// The consumer group name if present, or null if the <see cref="Headless.Messaging.Messages.Headers.Group"/> header is not set.
    /// </returns>
    public string? GetGroup()
    {
        return Headers.TryGetValue(Headless.Messaging.Messages.Headers.Group, out var value) ? value : null;
    }

    /// <summary>
    /// Attempts to retrieve the correlation ID from the message headers.
    /// The correlation ID links related messages in a message flow or saga pattern.
    /// </summary>
    /// <returns>
    /// The correlation ID if present, or null if the <see cref="Headless.Messaging.Messages.Headers.CorrelationId"/> header is not set.
    /// </returns>
    public string? GetCorrelationId()
    {
        return Headers.TryGetValue(Headless.Messaging.Messages.Headers.CorrelationId, out var value) ? value : null;
    }

    /// <summary>
    /// Attempts to retrieve the execution instance ID from the message headers.
    /// This ID identifies which application instance executed the message.
    /// </summary>
    /// <returns>
    /// The execution instance ID if present, or null if the <see cref="Headless.Messaging.Messages.Headers.ExecutionInstanceId"/> header is not set.
    /// </returns>
    public string? GetExecutionInstanceId()
    {
        return Headers.TryGetValue(Headless.Messaging.Messages.Headers.ExecutionInstanceId, out var value)
            ? value
            : null;
    }

    public bool Equals(TransportMessage other)
    {
        return Headers.Equals(other.Headers) && Body.Equals(other.Body);
    }

    public override bool Equals(object? obj)
    {
        return obj is TransportMessage other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Headers, Body);
    }

    public static bool operator ==(TransportMessage left, TransportMessage right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TransportMessage left, TransportMessage right)
    {
        return !(left == right);
    }
}
