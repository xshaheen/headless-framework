// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Serializer;
using Snappier;

namespace Headless.Core;

[PublicAPI]
public static class SnappyCompressor
{
    public static ReadOnlyMemory<byte> Compress<T>(T? result, JsonSerializerOptions? options = null)
    {
        options ??= JsonConstants.DefaultInternalJsonOptions;
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(result, options);
        var compressedMemory = Snappy.CompressToMemory(serializedBytes);

        return compressedMemory.Memory;
    }

    /// <summary>
    /// Compresses an object using Snappy with source-generated JSON metadata. AOT/trimming compatible.
    /// </summary>
    public static ReadOnlyMemory<byte> Compress<T>(T result, JsonTypeInfo<T> jsonTypeInfo)
    {
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(result, jsonTypeInfo);
        var compressedMemory = Snappy.CompressToMemory(serializedBytes);

        return compressedMemory.Memory;
    }

    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonSerializerOptions? options = null)
    {
        var bytes = Snappy.DecompressToMemory(compressed.Span);
        options ??= JsonConstants.DefaultInternalJsonOptions;
        var result = JsonSerializer.Deserialize<T>(bytes.Memory.Span, options);

        return result;
    }

    /// <summary>
    /// Decompresses Snappy-compressed data using source-generated JSON metadata. AOT/trimming compatible.
    /// </summary>
    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonTypeInfo<T> jsonTypeInfo)
    {
        var bytes = Snappy.DecompressToMemory(compressed.Span);
        return JsonSerializer.Deserialize(bytes.Memory.Span, jsonTypeInfo);
    }
}
