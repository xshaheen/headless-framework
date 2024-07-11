using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Snappier;

namespace Framework.BuildingBlocks.Helpers;

[PublicAPI]
public static class SnappyCompressor
{
    public static ReadOnlyMemory<byte> Compress<T>(T? result, JsonSerializerOptions? options = null)
    {
        options ??= PlatformJsonConstants.DefaultInternalJsonOptions;
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(result, options);
        var compressedMemory = Snappy.CompressToMemory(serializedBytes);

        return compressedMemory.Memory;
    }

    public static T? Decompress<T>(ReadOnlyMemory<byte> compressed, JsonSerializerOptions? options = null)
    {
        var bytes = Snappy.DecompressToMemory(compressed.Span);
        options ??= PlatformJsonConstants.DefaultInternalJsonOptions;
        var result = JsonSerializer.Deserialize<T>(bytes.Memory.Span, options);

        return result;
    }
}
