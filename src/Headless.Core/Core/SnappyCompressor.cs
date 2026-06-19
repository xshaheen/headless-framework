// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json.Serialization.Metadata;
using Headless.Serializer;
using Snappier;

namespace Headless.Core;

[PublicAPI]
public static class SnappyCompressor
{
    [MustDisposeResource]
    public static IMemoryOwner<byte> Compress<T>(T? result, JsonSerializerOptions? options = null)
    {
        options ??= JsonConstants.DefaultInternalJsonOptions;
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(result, options);

        return Snappy.CompressToMemory(serializedBytes);
    }

    /// <summary>
    /// Compresses an object using Snappy with source-generated JSON metadata. AOT/trimming compatible.
    /// </summary>
    [MustDisposeResource]
    public static IMemoryOwner<byte> Compress<T>(T result, JsonTypeInfo<T> jsonTypeInfo)
    {
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(result, jsonTypeInfo);

        return Snappy.CompressToMemory(serializedBytes);
    }

    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonSerializerOptions? options = null)
    {
        // DecompressToMemory rents a pooled buffer the caller must dispose; deserialization reads
        // the span synchronously within this scope, so disposing on return is safe.
        using var bytes = Snappy.DecompressToMemory(compressed.Span);
        options ??= JsonConstants.DefaultInternalJsonOptions;

        return JsonSerializer.Deserialize<T>(bytes.Memory.Span, options);
    }

    /// <summary>
    /// Decompresses Snappy-compressed data using source-generated JSON metadata. AOT/trimming compatible.
    /// </summary>
    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var bytes = Snappy.DecompressToMemory(compressed.Span);

        return JsonSerializer.Deserialize(bytes.Memory.Span, jsonTypeInfo);
    }
}
