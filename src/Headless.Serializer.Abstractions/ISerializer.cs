// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

/// <summary>
/// Provider-agnostic contract for serializing and deserializing objects to and from a <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// The interface intentionally operates on streams to stay format-neutral and to avoid intermediate byte
/// allocations on hot paths. Use the extension methods in <see cref="SerializerExtensions"/> when a
/// <c>byte[]</c> or <c>string</c> is more convenient.
/// </remarks>
public interface ISerializer
{
    /// <summary>Deserializes the content of <paramref name="data"/> into an instance of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">A stream whose current position is the start of the serialized payload.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
    T? Deserialize<T>(Stream data);

    /// <summary>Serializes <paramref name="value"/> into <paramref name="output"/>.</summary>
    /// <typeparam name="T">The static type of the value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="output">The stream to write the serialized payload to.</param>
    void Serialize<T>(T value, Stream output);

    /// <summary>Deserializes the content of <paramref name="data"/> into an instance of <paramref name="objectType"/>.</summary>
    /// <param name="data">A stream whose current position is the start of the serialized payload.</param>
    /// <param name="objectType">The target <see cref="Type"/>.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
    object? Deserialize(Stream data, Type objectType);

    /// <summary>Serializes <paramref name="value"/> into <paramref name="output"/> using the runtime type of the value.</summary>
    /// <param name="value">The value to serialize. When <see langword="null"/>, the output encodes a null/absent value.</param>
    /// <param name="output">The stream to write the serialized payload to.</param>
    void Serialize(object? value, Stream output);
}

/// <summary>
/// Marker interface for serializers that produce binary (non-text) output, such as MessagePack or Protocol Buffers.
/// </summary>
/// <remarks>
/// <see cref="SerializerExtensions.SerializeToString{T}"/> encodes binary output as Base64 when the serializer
/// does not implement <see cref="ITextSerializer"/>.
/// </remarks>
public interface IBinarySerializer : ISerializer;

/// <summary>
/// Marker interface for serializers that produce UTF-8 text output, such as JSON or XML.
/// </summary>
/// <remarks>
/// <see cref="SerializerExtensions.SerializeToString{T}"/> returns the raw UTF-8 string for text serializers,
/// and <see cref="SerializerExtensions.Deserialize{T}(ISerializer, string)"/> decodes the input as UTF-8 bytes
/// rather than Base64.
/// </remarks>
public interface ITextSerializer : ISerializer;

/// <summary>Marker interface for JSON serializers.</summary>
public interface IJsonSerializer : ITextSerializer;
