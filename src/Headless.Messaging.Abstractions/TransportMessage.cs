// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Represents a message in transit between the message broker and application.
/// This struct encapsulates the message headers (metadata) and body (serialized content) as received from or sent to a broker.
/// </summary>
/// <remarks>
/// This is a value type optimized for performance when passing messages through the message processing pipeline.
/// Unlike the storage message model, this struct works with raw byte data rather than deserialized objects,
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
[PublicAPI]
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
    /// <returns>The message ID stored in the <see cref="Messaging.Headers.MessageId"/> header.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the MessageId header is not present.</exception>
    public string GetId()
    {
        return Headers[Messaging.Headers.MessageId]!;
    }

    /// <summary>
    /// Retrieves the message name from the message headers.
    /// </summary>
    /// <returns>The message name stored in the <see cref="Messaging.Headers.MessageName"/> header.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the MessageName header is not present.</exception>
    public string GetName()
    {
        return Headers[Messaging.Headers.MessageName]!;
    }

    /// <summary>
    /// Attempts to retrieve the consumer group name from the message headers.
    /// </summary>
    /// <returns>
    /// The consumer group name if present, or null if the <see cref="Messaging.Headers.Group"/> header is not set.
    /// </returns>
    public string? GetGroup()
    {
        return Headers.TryGetValue(Messaging.Headers.Group, out var value) ? value : null;
    }

    /// <summary>
    /// Attempts to retrieve the correlation ID from the message headers.
    /// The correlation ID links related messages in a message flow or saga pattern.
    /// </summary>
    /// <returns>
    /// The correlation ID if present, or null if the <see cref="Messaging.Headers.CorrelationId"/> header is not set.
    /// </returns>
    public string? GetCorrelationId()
    {
        return Headers.TryGetValue(Messaging.Headers.CorrelationId, out var value) ? value : null;
    }

    /// <summary>
    /// Attempts to retrieve the execution instance ID from the message headers.
    /// This ID identifies which application instance executed the message.
    /// </summary>
    /// <returns>
    /// The execution instance ID if present, or null if the <see cref="Messaging.Headers.ExecutionInstanceId"/> header is not set.
    /// </returns>
    public string? GetExecutionInstanceId()
    {
        return Headers.TryGetValue(Messaging.Headers.ExecutionInstanceId, out var value) ? value : null;
    }

    /// <inheritdoc />
    public bool Equals(TransportMessage other)
    {
        if (Headers.Count != other.Headers.Count)
        {
            return false;
        }

        foreach (var pair in Headers)
        {
            if (!other.Headers.TryGetValue(pair.Key, out var otherValue))
            {
                return false;
            }

            if (!string.Equals(pair.Value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return Body.Span.SequenceEqual(other.Body.Span);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TransportMessage other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Equals is order-independent (TryGetValue per key); GetHashCode must also be
        // order-independent so the equality contract holds for dictionaries with the same
        // content but different insertion order. XOR is commutative, so pair hashes can be
        // accumulated in any traversal order without affecting the final value.
        var pairHash = 0;

        foreach (var pair in Headers)
        {
            var keyHash = StringComparer.Ordinal.GetHashCode(pair.Key);
            var valueHash = pair.Value is null ? 0 : StringComparer.Ordinal.GetHashCode(pair.Value);
            pairHash ^= HashCode.Combine(keyHash, valueHash);
        }

        // Sample up to 8 bytes of the body (first 4 + last 4) into the hash so messages with
        // the same length but different content distribute across hash buckets. Equals still
        // enforces full-span equality; this just sharpens the bucketing.
        return HashCode.Combine(Body.Length, Headers.Count, pairHash, _SampleBodyHash(Body.Span));
    }

    private static int _SampleBodyHash(ReadOnlySpan<byte> body)
    {
        if (body.Length >= 8)
        {
            var head = BinaryPrimitives.ReadInt32LittleEndian(body);
            var tail = BinaryPrimitives.ReadInt32LittleEndian(body[^4..]);
            return HashCode.Combine(head, tail);
        }

        if (body.Length >= 4)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(body);
        }

        var sample = 0;
        for (var i = 0; i < body.Length; i++)
        {
            sample = HashCode.Combine(sample, (int)body[i]);
        }

        return sample;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> equals <paramref name="right"/>.</summary>
    public static bool operator ==(TransportMessage left, TransportMessage right)
    {
        return left.Equals(right);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> does not equal <paramref name="right"/>.</summary>
    public static bool operator !=(TransportMessage left, TransportMessage right)
    {
        return !(left == right);
    }
}
