// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Serializer;

/// <summary>
/// Provider-agnostic contract for serializing and deserializing objects to and from byte buffers.
/// </summary>
/// <remarks>
/// The interface operates on buffer primitives — <see cref="IBufferWriter{T}"/> for writes and
/// <see cref="ReadOnlyMemory{T}"/> / <see cref="ReadOnlySequence{T}"/> for reads — so each implementation can use
/// its backend's lowest-allocation path (<c>System.Text.Json</c>'s <c>Utf8JsonWriter</c>/<c>Utf8JsonReader</c>,
/// MessagePack's <c>IBufferWriter</c>/<c>ReadOnlySequence</c> APIs) without intermediate <c>byte[]</c> or
/// <see cref="Stream"/> materialization. Use the extension methods in <see cref="SerializerExtensions"/> when a
/// <c>byte[]</c>, <see langword="string"/>, or <see cref="Stream"/> is more convenient.
/// </remarks>
public interface ISerializer
{
    /// <summary>Serializes <paramref name="value"/> into <paramref name="output"/>.</summary>
    /// <typeparam name="T">The static type of the value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="output">The buffer writer the serialized payload is written to.</param>
    void Serialize<T>(T value, IBufferWriter<byte> output);

    /// <summary>Serializes <paramref name="value"/> into <paramref name="output"/> using the runtime type of the value.</summary>
    /// <param name="value">The value to serialize. When <see langword="null"/>, the output encodes a null/absent value.</param>
    /// <param name="output">The buffer writer the serialized payload is written to.</param>
    void Serialize(object? value, IBufferWriter<byte> output);

    /// <summary>Deserializes the contiguous <paramref name="data"/> into an instance of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">The raw serialized payload as a contiguous buffer.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
    T? Deserialize<T>(ReadOnlyMemory<byte> data);

    /// <summary>Deserializes the possibly non-contiguous <paramref name="data"/> into an instance of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">The raw serialized payload as a (possibly multi-segment) sequence, e.g. from a <c>PipeReader</c>.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
    T? Deserialize<T>(in ReadOnlySequence<byte> data);

    /// <summary>Deserializes the contiguous <paramref name="data"/> into an instance of <paramref name="type"/>.</summary>
    /// <param name="data">The raw serialized payload as a contiguous buffer.</param>
    /// <param name="type">The target <see cref="Type"/>.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);

    /// <summary>Deserializes the possibly non-contiguous <paramref name="data"/> into an instance of <paramref name="type"/>.</summary>
    /// <param name="data">The raw serialized payload as a (possibly multi-segment) sequence, e.g. from a <c>PipeReader</c>.</param>
    /// <param name="type">The target <see cref="Type"/>.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload represents a null/absent value.</returns>
    object? Deserialize(in ReadOnlySequence<byte> data, Type type);
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
