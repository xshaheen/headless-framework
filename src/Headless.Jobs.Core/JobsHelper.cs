// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Compression;
using Headless.Checks;

namespace Headless.Jobs;

/// <summary>
/// Serialization helpers for converting job request payloads to and from the byte array representation
/// stored in the persistence layer. Supports optional GZip compression.
/// </summary>
public static class JobsHelper
{
    internal const int DefaultMaxDecompressedRequestBytes = 64 * 1024 * 1024;
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

    /// <summary>Maximum expanded size of a compressed job request. Defaults to 64 MiB.</summary>
    public static int MaxDecompressedRequestBytes { get; set; } = DefaultMaxDecompressedRequestBytes;

    /// <summary>
    /// Serializes <paramref name="data"/> to the byte array format used by the persistence layer,
    /// applying GZip compression when <see cref="UseGZipCompression"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the request payload.</typeparam>
    /// <param name="data">The value to serialize.</param>
    /// <returns>
    /// A UTF-8 JSON byte array when compression is disabled, or a GZip-compressed byte array with a
    /// four-byte GZip signature appended as a sentinel when compression is enabled.
    /// </returns>
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
            using (var stream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
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

    /// <summary>
    /// Deserializes a job request payload from its stored byte array form.
    /// </summary>
    /// <typeparam name="T">The expected request type.</typeparam>
    /// <param name="gzipBytes">The raw bytes from the persistence layer.</param>
    /// <returns>The deserialized value, or <see langword="default"/> when the JSON is null/empty.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="UseGZipCompression"/> is <see langword="true"/> but the bytes lack the expected GZip sentinel.
    /// </exception>
    public static T? ReadJobRequest<T>(byte[] gzipBytes)
    {
        var serializedObject = ReadJobRequestAsString(gzipBytes);

        return JsonSerializer.Deserialize<T>(serializedObject, RequestJsonSerializerOptions);
    }

    /// <summary>
    /// Reads a job request payload as its raw JSON string without deserializing it.
    /// </summary>
    /// <param name="gzipBytes">The raw bytes from the persistence layer.</param>
    /// <returns>The UTF-8 JSON string representation of the stored payload.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="UseGZipCompression"/> is <see langword="true"/> but the bytes lack the expected GZip sentinel.
    /// </exception>
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

        using var memoryStream = new MemoryStream(gzipBytes, 0, gzipBytes.Length - _GZipSignature.Length);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var expandedStream = new MemoryStream();
        var buffer = new byte[81920];
        var maxDecompressedBytes = Argument.IsPositive(MaxDecompressedRequestBytes);

        while (true)
        {
            var read = gzipStream.Read(buffer);
            if (read == 0)
            {
                break;
            }

            if (expandedStream.Length > maxDecompressedBytes - read)
            {
                throw new InvalidDataException(
                    $"The decompressed job request exceeds the configured {maxDecompressedBytes} byte limit."
                );
            }

            expandedStream.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(expandedStream.GetBuffer(), 0, checked((int)expandedStream.Length));
    }
}
