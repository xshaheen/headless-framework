using System.IO.Compression;

namespace Headless.Jobs;

public static class JobsHelper
{
    private static readonly byte[] _GZipSignature = [0x1f, 0x8b, 0x08, 0x00];

    /// <summary>
    /// JsonSerializerOptions specifically for job request serialization/deserialization.
    /// Can be configured during application startup via JobsOptionsBuilder.
    /// </summary>
    public static JsonSerializerOptions RequestJsonSerializerOptions { get; set; } = new();

    /// <summary>
    /// Controls whether job requests are GZip-compressed.
    /// When false (default), requests are stored as plain UTF-8 JSON bytes without compression.
    /// </summary>
    public static bool UseGZipCompression { get; set; }

    public static byte[] CreateJobRequest<T>(T data)
    {
        // If data is already a byte array, short-circuit where possible
        if (data is byte[] existingBytes)
        {
            // If compression is enabled and data already has the GZip signature, assume it is in the final format
            if (
                UseGZipCompression
                && existingBytes.Length >= _GZipSignature.Length
                && existingBytes.TakeLast(_GZipSignature.Length).SequenceEqual(_GZipSignature)
            )
            {
                return existingBytes;
            }

            // If compression is disabled, treat the provided bytes as the final representation
            if (!UseGZipCompression)
            {
                return existingBytes;
            }
        }

        var serialized = data is byte[] bytes
            ? bytes
            : JsonSerializer.SerializeToUtf8Bytes(data, RequestJsonSerializerOptions);

        if (!UseGZipCompression)
        {
            return serialized;
        }

        Span<byte> compressedBytes;
        using (var memoryStream = new MemoryStream())
        {
            using (var stream = new GZipStream(memoryStream, CompressionMode.Compress, true))
            {
                stream.Write(serialized);
            }

            compressedBytes = memoryStream.GetBuffer().AsSpan()[..(int)memoryStream.Length];
        }

        var returnVal = new byte[compressedBytes.Length + _GZipSignature.Length];
        var returnValSpan = returnVal.AsSpan();
        compressedBytes.CopyTo(returnValSpan);
        _GZipSignature.AsSpan().CopyTo(returnValSpan[compressedBytes.Length..]);

        return returnVal;
    }

    public static T? ReadJobRequest<T>(byte[] gzipBytes)
    {
        var serializedObject = ReadJobRequestAsString(gzipBytes);

        return JsonSerializer.Deserialize<T>(serializedObject, RequestJsonSerializerOptions);
    }

    public static string ReadJobRequestAsString(byte[] gzipBytes)
    {
        if (!UseGZipCompression)
        {
            // When compression is disabled, treat the bytes as plain UTF-8 JSON
            return Encoding.UTF8.GetString(gzipBytes);
        }

        if (!gzipBytes.TakeLast(_GZipSignature.Length).SequenceEqual(_GZipSignature))
        {
            throw new InvalidOperationException("The bytes are not GZip compressed.");
        }

        var compressedBytes = gzipBytes.Take(gzipBytes.Length - _GZipSignature.Length).ToArray();

        using var memoryStream = new MemoryStream(compressedBytes);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var streamReader = new StreamReader(gzipStream);

        var serializedObject = streamReader.ReadToEnd();

        return serializedObject;
    }
}
