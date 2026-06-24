// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json.Serialization.Metadata;
using Headless.Serializer;
using Snappier;

namespace Headless.Core;

/// <summary>
/// Provides static helpers for serializing .NET objects to JSON and compressing the result with
/// Snappy, and for the inverse: decompressing Snappy data and deserializing the JSON payload.
/// The caller owns the returned <see cref="IMemoryOwner{T}" /> and must dispose it.
/// </summary>
[PublicAPI]
public static class SnappyCompressor
{
    /// <summary>
    /// Serializes <paramref name="result" /> to UTF-8 JSON and compresses the bytes using Snappy.
    /// Uses reflection-based JSON serialization; prefer the <see cref="Compress{T}(T, JsonTypeInfo{T})" />
    /// overload in AOT or trimming scenarios.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="result">The value to serialize. May be <see langword="null" />.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions" />. When <see langword="null" />, the framework's
    /// default internal options are used.
    /// </param>
    /// <returns>
    /// An <see cref="IMemoryOwner{T}" /> wrapping the Snappy-compressed bytes. The caller must
    /// dispose this instance to return the underlying buffer to the pool.
    /// </returns>
    [MustDisposeResource]
    public static IMemoryOwner<byte> Compress<T>(T? result, JsonSerializerOptions? options = null)
    {
        options ??= JsonConstants.DefaultInternalJsonOptions;

        // Serialize straight into a pooled buffer and hand the written span to Snappy, avoiding the intermediate
        // right-sized byte[] that SerializeToUtf8Bytes allocates and immediately discards.
        using var buffer = new PooledByteBufferWriter();

        using (var writer = new Utf8JsonWriter(buffer, _WriterOptionsFor(options)))
        {
            JsonSerializer.Serialize(writer, result, options);
        }

        return Snappy.CompressToMemory(buffer.WrittenSpan);
    }

    /// <summary>
    /// Serializes <paramref name="result" /> to UTF-8 JSON using source-generated metadata and
    /// compresses the bytes using Snappy. AOT/trimming compatible.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="result">The value to serialize.</param>
    /// <param name="jsonTypeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}" /> that describes how to serialize
    /// <typeparamref name="T" /> without reflection.
    /// </param>
    /// <returns>
    /// An <see cref="IMemoryOwner{T}" /> wrapping the Snappy-compressed bytes. The caller must
    /// dispose this instance to return the underlying buffer to the pool.
    /// </returns>
    [MustDisposeResource]
    public static IMemoryOwner<byte> Compress<T>(T result, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var buffer = new PooledByteBufferWriter();

        using (var writer = new Utf8JsonWriter(buffer, _WriterOptionsFor(jsonTypeInfo.Options)))
        {
            JsonSerializer.Serialize(writer, result, jsonTypeInfo);
        }

        return Snappy.CompressToMemory(buffer.WrittenSpan);
    }

    /// <summary>
    /// Decompresses Snappy-compressed data and deserializes the resulting UTF-8 JSON into a value of
    /// type <typeparamref name="T" />. Uses reflection-based JSON deserialization; prefer the
    /// <see cref="Decompress{T}(ReadOnlyMemory{byte}, JsonTypeInfo{T})" /> overload in AOT or
    /// trimming scenarios.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="compressed">The Snappy-compressed bytes to decompress and deserialize.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions" />. When <see langword="null" />, the framework's
    /// default internal options are used.
    /// </param>
    /// <returns>
    /// The deserialized value, or <see langword="null" /> if the JSON payload represents a JSON
    /// <c>null</c>.
    /// </returns>
    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonSerializerOptions? options = null)
    {
        // DecompressToMemory rents a pooled buffer the caller must dispose; deserialization reads
        // the span synchronously within this scope, so disposing on return is safe.
        using var bytes = Snappy.DecompressToMemory(compressed.Span);
        options ??= JsonConstants.DefaultInternalJsonOptions;

        return JsonSerializer.Deserialize<T>(bytes.Memory.Span, options);
    }

    /// <summary>
    /// Decompresses Snappy-compressed data and deserializes the resulting UTF-8 JSON into a value of
    /// type <typeparamref name="T" /> using source-generated metadata. AOT/trimming compatible.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="compressed">The Snappy-compressed bytes to decompress and deserialize.</param>
    /// <param name="jsonTypeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}" /> that describes how to deserialize
    /// <typeparamref name="T" /> without reflection.
    /// </param>
    /// <returns>
    /// The deserialized value, or <see langword="null" /> if the JSON payload represents a JSON
    /// <c>null</c>.
    /// </returns>
    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var bytes = Snappy.DecompressToMemory(compressed.Span);

        return JsonSerializer.Deserialize(bytes.Memory.Span, jsonTypeInfo);
    }

    // A pre-made Utf8JsonWriter governs its own formatting and limits (indentation, encoder, depth) independently
    // of the JsonSerializerOptions passed to JsonSerializer.Serialize, so the writer must inherit those settings or
    // the configured escaping/indentation/depth limit would be silently ignored — keeping the output byte-identical
    // to SerializeToUtf8Bytes. Mirrors Headless.Serializer's SystemJsonSerializer.
    private static JsonWriterOptions _WriterOptionsFor(JsonSerializerOptions options)
    {
        return new JsonWriterOptions
        {
            Encoder = options.Encoder,
            Indented = options.WriteIndented,
            IndentCharacter = options.IndentCharacter,
            IndentSize = options.IndentSize,
            NewLine = options.NewLine,
            MaxDepth = options.MaxDepth,
        };
    }
}
