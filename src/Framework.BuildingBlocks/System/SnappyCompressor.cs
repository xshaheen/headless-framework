// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;
using Snappier;

namespace Framework.BuildingBlocks.System;

[PublicAPI]
public static class SnappyCompressor
{
    public static ReadOnlyMemory<byte> Compress<T>(T? result, JsonSerializerOptions? options = null)
    {
        options ??= FrameworkJsonConstants.DefaultInternalJsonOptions;
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(result, options);
        var compressedMemory = Snappy.CompressToMemory(serializedBytes);

        return compressedMemory.Memory;
    }

    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonSerializerOptions? options = null)
    {
        var bytes = Snappy.DecompressToMemory(compressed.Span);
        options ??= FrameworkJsonConstants.DefaultInternalJsonOptions;
        var result = JsonSerializer.Deserialize<T>(bytes.Memory.Span, options);

        return result;
    }
}
