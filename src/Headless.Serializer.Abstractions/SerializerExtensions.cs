// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Serializer;

/// <summary>
/// Convenience adapters for <see cref="ISerializer"/> that operate on <c>byte[]</c>, <c>string</c>, and
/// <see cref="Stream"/> instead of the core buffer primitives.
/// </summary>
/// <remarks>
/// These adapters bridge the buffer-first contract to the shapes consumers usually hold. Writes serialize into a
/// pooled <see cref="IBufferWriter{T}"/> and copy out exactly once; reads wrap the input as a
/// <see cref="ReadOnlyMemory{T}"/> or <see cref="ReadOnlySequence{T}"/> so the serializer reads in place without an
/// intermediate <see cref="MemoryStream"/>.
/// </remarks>
public static class SerializerExtensions
{
    extension(ISerializer serializer)
    {
        /// <summary>Deserializes <paramref name="data"/> into an instance of <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="data">The raw serialized bytes.</param>
        /// <returns>The deserialized value, or <see langword="null"/> for a null/absent payload.</returns>
        public T? Deserialize<T>(byte[] data)
        {
            return serializer.Deserialize<T>(data.AsMemory());
        }

        /// <summary>
        /// Deserializes a string representation into an instance of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="data">
        /// The serialized payload as a string. For <see cref="ITextSerializer"/> implementations (e.g. JSON) the
        /// string is decoded as UTF-8 bytes. For <see cref="IBinarySerializer"/> implementations (e.g. MessagePack)
        /// the string is treated as Base64. Pass <see langword="null"/> to get the default value of
        /// <typeparamref name="T"/>.
        /// </param>
        /// <returns>The deserialized value, or <see langword="null"/> when <paramref name="data"/> is <see langword="null"/>.</returns>
        public T? Deserialize<T>(string? data)
        {
            if (data is null)
            {
                return default;
            }

            if (serializer is not ITextSerializer)
            {
                return serializer.Deserialize<T>(Convert.FromBase64String(data));
            }

            // Text serializers consume UTF-8 directly. Rent a pooled buffer for the transcoded bytes instead of
            // allocating a throwaway array per call, then read in place from the written span.
            var maxByteCount = Encoding.UTF8.GetMaxByteCount(data.Length);
            var rented = ArrayPool<byte>.Shared.Rent(maxByteCount);

            try
            {
                var written = Encoding.UTF8.GetBytes(data, rented);
                return serializer.Deserialize<T>(rented.AsMemory(0, written));
            }
            finally
            {
                // Clear the transcoded payload before the rental rejoins the shared pool (see PooledByteBufferWriter).
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }

        /// <summary>Deserializes the content of <paramref name="data"/> into an instance of <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="data">A stream whose current position is the start of the serialized payload.</param>
        /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
        public T? Deserialize<T>(Stream data)
        {
            return serializer.Deserialize<T>(_ReadToSequence(data));
        }

        /// <summary>Deserializes the content of <paramref name="data"/> into an instance of <paramref name="type"/>.</summary>
        /// <param name="data">A stream whose current position is the start of the serialized payload.</param>
        /// <param name="type">The target <see cref="Type"/>.</param>
        /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
        public object? Deserialize(Stream data, Type type)
        {
            return serializer.Deserialize(_ReadToSequence(data), type);
        }

        /// <summary>Serializes <paramref name="value"/> and returns the result as a byte array.</summary>
        /// <typeparam name="T">The static type of the value being serialized.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>The serialized bytes, or <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.</returns>
        public byte[]? SerializeToBytes<T>(T? value)
        {
            if (value is null)
            {
                return null;
            }

            using var writer = new PooledByteBufferWriter();
            serializer.Serialize(value, writer);

            return writer.WrittenSpan.ToArray();
        }

        /// <summary>Serializes <paramref name="value"/> into <paramref name="output"/>.</summary>
        /// <typeparam name="T">The static type of the value being serialized.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="output">The stream the serialized payload is written to.</param>
        public void Serialize<T>(T value, Stream output)
        {
            using var writer = new PooledByteBufferWriter();
            serializer.Serialize(value, writer);
            output.Write(writer.WrittenSpan);
        }

        /// <summary>Serializes <paramref name="value"/> into <paramref name="output"/> using the runtime type of the value.</summary>
        /// <param name="value">The value to serialize. When <see langword="null"/>, the output encodes a null/absent value.</param>
        /// <param name="output">The stream the serialized payload is written to.</param>
        public void Serialize(object? value, Stream output)
        {
            using var writer = new PooledByteBufferWriter();
            serializer.Serialize(value, writer);
            output.Write(writer.WrittenSpan);
        }

        /// <summary>
        /// Serializes <paramref name="value"/> and returns the result as a string.
        /// </summary>
        /// <typeparam name="T">The static type of the value being serialized.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>
        /// For <see cref="ITextSerializer"/> implementations (e.g. JSON), the raw UTF-8 string. For
        /// <see cref="IBinarySerializer"/> implementations (e.g. MessagePack), a Base64-encoded string.
        /// Returns <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.
        /// </returns>
        public string? SerializeToString<T>(T? value)
        {
            if (value is null)
            {
                return null;
            }

            using var writer = new PooledByteBufferWriter();
            serializer.Serialize(value, writer);

            return serializer is ITextSerializer
                ? Encoding.UTF8.GetString(writer.WrittenSpan)
                : Convert.ToBase64String(writer.WrittenSpan);
        }
    }

    // Reads a stream into a contiguous pooled buffer wrapped as a single-segment ReadOnlySequence. Streams are a
    // legacy/interop shape for this contract (the byte/buffer overloads are the fast path), so the simplest correct
    // bridge is preferred over multi-segment streaming.
    private static ReadOnlySequence<byte> _ReadToSequence(Stream data)
    {
        if (data is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var segment))
        {
            // Honor the stream's current position — the contract is "position is the start of the payload", and the
            // non-MemoryStream branch (and the old Stream serializers) read from the position, not from offset 0.
            var position = (int)memoryStream.Position;
            var sequence = new ReadOnlySequence<byte>(
                segment.Array!,
                segment.Offset + position,
                segment.Count - position
            );

            // Consume the stream like the read loop below (and the old Stream serializers) so a caller that reads on
            // after deserializing sees the payload as consumed rather than re-reading it.
            memoryStream.Position = memoryStream.Length;

            return sequence;
        }

        // Size the initial rental from the bytes that remain after the current position, not the whole stream — a
        // small payload near the end of a large seekable stream must not rent a buffer sized for the entire stream.
        using var writer = new PooledByteBufferWriter(
            data.CanSeek ? (int)Math.Min(data.Length - data.Position, int.MaxValue) : 256
        );

        int read;

        do
        {
            var span = writer.GetSpan();
            read = data.Read(span);
            writer.Advance(read);
        } while (read > 0);

        return new ReadOnlySequence<byte>(writer.WrittenSpan.ToArray());
    }
}
