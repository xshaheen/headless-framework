// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Jobs;

/// <summary>
/// Serialization helpers for converting job request payloads to and from the byte array representation
/// stored in the persistence layer. Supports optional GZip compression. Stateless: every member takes the
/// per-host <see cref="JobsRequestSerializationOptions"/> composed by <c>AddHeadlessJobs</c> and resolved
/// from the host's service provider — there is no process-global serializer state.
/// </summary>
public static class JobsHelper
{
    private static readonly byte[] _GZipSignature = [0x1f, 0x8b, 0x08, 0x00];

    // Reads a sentinel-signed payload whatever this host's own compression setting, under the default expansion cap.
    private static readonly JobsRequestSerializationOptions _CompressedPayloadReadOptions = new()
    {
        UseGZipCompression = true,
    };

    /// <summary>
    /// Serializes <paramref name="data"/> to the byte array format used by the persistence layer,
    /// applying GZip compression when <see cref="JobsRequestSerializationOptions.UseGZipCompression"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the request payload.</typeparam>
    /// <param name="data">The value to serialize.</param>
    /// <param name="options">The per-host request serialization settings.</param>
    /// <returns>
    /// A UTF-8 JSON byte array when compression is disabled, or a GZip-compressed byte array with a
    /// four-byte GZip signature appended as a sentinel when compression is enabled.
    /// </returns>
    public static byte[] CreateJobRequest<T>(T data, JobsRequestSerializationOptions options)
    {
        Argument.IsNotNull(options);

        if (data is byte[] existingBytes && _TryPassThroughBytes(existingBytes, options, out var passthrough))
        {
            return passthrough;
        }

        var serialized = data is byte[] bytes
            ? bytes
            : JsonSerializer.SerializeToUtf8Bytes(data, JobsRequestJson.TypeInfo<T>(options.SerializerOptions));

        return _CompressIfEnabled(serialized, options);
    }

    /// <summary>
    /// Serializes <paramref name="data"/> as its declared <paramref name="inputType"/> to the persistence byte array
    /// format, applying GZip compression when
    /// <see cref="JobsRequestSerializationOptions.UseGZipCompression"/> is <see langword="true"/>. The non-generic
    /// counterpart of <see cref="CreateJobRequest{T}"/>, used by the chain enqueue path where a step captures its
    /// payload as <see cref="object"/> plus a runtime <see cref="Type"/>; serializing as
    /// <paramref name="inputType"/> keeps the stored JSON identical to the typed overload.
    /// </summary>
    /// <param name="data">The value to serialize.</param>
    /// <param name="inputType">The declared type to serialize <paramref name="data"/> as.</param>
    /// <param name="options">The per-host request serialization settings.</param>
    /// <returns>
    /// A UTF-8 JSON byte array when compression is disabled, or a GZip-compressed byte array with a four-byte GZip
    /// signature appended as a sentinel when compression is enabled.
    /// </returns>
    public static byte[] CreateJobRequest(object data, Type inputType, JobsRequestSerializationOptions options)
    {
        Argument.IsNotNull(data);
        Argument.IsNotNull(inputType);
        Argument.IsNotNull(options);

        if (data is byte[] existingBytes && _TryPassThroughBytes(existingBytes, options, out var passthrough))
        {
            return passthrough;
        }

        var serialized = data is byte[] bytes
            ? bytes
            : JsonSerializer.SerializeToUtf8Bytes(data, options.SerializerOptions.GetTypeInfo(inputType));

        return _CompressIfEnabled(serialized, options);
    }

    // Short-circuits when the caller already handed us the final byte representation: compression-off passes any
    // byte[] through verbatim, and compression-on passes an already-signed GZip payload through untouched.
    private static bool _TryPassThroughBytes(
        byte[] existingBytes,
        JobsRequestSerializationOptions options,
        out byte[] result
    )
    {
        if (!options.UseGZipCompression || _HasGZipSentinel(existingBytes))
        {
            result = existingBytes;
            return true;
        }

        result = [];
        return false;
    }

    private static byte[] _CompressIfEnabled(byte[] serialized, JobsRequestSerializationOptions options)
    {
        if (!options.UseGZipCompression)
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
    /// <param name="options">The per-host request serialization settings.</param>
    /// <returns>The deserialized value, or <see langword="default"/> when the JSON value is <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="JobsRequestSerializationOptions.UseGZipCompression"/> is <see langword="true"/> but the
    /// bytes lack the expected GZip sentinel.
    /// </exception>
    /// <exception cref="InvalidDataException">The compressed payload is truncated or otherwise invalid.</exception>
    /// <exception cref="JsonException">The JSON payload is empty, malformed, or incompatible with <typeparamref name="T"/>.</exception>
    public static T? ReadJobRequest<T>(byte[] gzipBytes, JobsRequestSerializationOptions options)
    {
        Argument.IsNotNull(options);

        if (!options.UseGZipCompression)
        {
            return JsonSerializer.Deserialize(gzipBytes, JobsRequestJson.TypeInfo<T>(options.SerializerOptions));
        }

        using var memoryStream = _OpenCompressedPayload(gzipBytes);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);

        return JsonSerializer.Deserialize(gzipStream, JobsRequestJson.TypeInfo<T>(options.SerializerOptions));
    }

    /// <summary>
    /// Reads a job request payload as its raw JSON string without deserializing it. Compressed payloads
    /// whose expanded size exceeds <see cref="JobsRequestSerializationOptions.MaxDecompressedRequestBytes"/>
    /// are rejected.
    /// </summary>
    /// <param name="gzipBytes">The raw bytes from the persistence layer.</param>
    /// <param name="options">The per-host request serialization settings.</param>
    /// <returns>The UTF-8 JSON string representation of the stored payload.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="JobsRequestSerializationOptions.UseGZipCompression"/> is <see langword="true"/> but the
    /// bytes lack the expected GZip sentinel.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The compressed payload is truncated or otherwise invalid, or its expanded size exceeds
    /// <see cref="JobsRequestSerializationOptions.MaxDecompressedRequestBytes"/>.
    /// </exception>
    public static string ReadJobRequestAsString(byte[] gzipBytes, JobsRequestSerializationOptions options)
    {
        Argument.IsNotNull(options);

        if (!options.UseGZipCompression)
        {
            // When compression is disabled, treat the bytes as plain UTF-8 JSON
            return Encoding.UTF8.GetString(gzipBytes);
        }

        using var memoryStream = _OpenCompressedPayload(gzipBytes);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var expandedStream = new MemoryStream();
        var buffer = new byte[81920];
        var maxDecompressedBytes = Argument.IsPositive(options.MaxDecompressedRequestBytes);

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
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The decompressed job request exceeds the configured {maxDecompressedBytes} byte limit."
                    )
                );
            }

            expandedStream.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(expandedStream.GetBuffer(), 0, checked((int)expandedStream.Length));
    }

    /// <summary>
    /// Computes the canonical hash of a stored request payload without knowing its request type, so two payloads that
    /// carry the same arguments compare equal however the writing host serialized them. A payload that ends in the GZip
    /// sentinel is decompressed first, whatever the reading host's compression setting. The JSON is then rewritten with
    /// object members in ordinal name order and no insignificant whitespace, and hashed.
    /// </summary>
    /// <param name="payload">The stored request bytes; empty for a job that takes no arguments.</param>
    /// <returns>
    /// The lowercase hexadecimal SHA-256 of the canonical JSON, of the empty sequence for an empty payload, or
    /// <see langword="null"/> when the payload is neither valid JSON nor valid compressed JSON.
    /// </returns>
    internal static string? TryComputeCanonicalRequestHash(byte[] payload)
    {
        Argument.IsNotNull(payload);

        if (payload.Length == 0)
        {
            return Convert.ToHexStringLower(SHA256.HashData([]));
        }

        try
        {
            using var document = _HasGZipSentinel(payload)
                ? JsonDocument.Parse(ReadJobRequestAsString(payload, _CompressedPayloadReadOptions))
                : JsonDocument.Parse(payload);
            var canonical = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(canonical))
            {
                _WriteCanonical(writer, document.RootElement);
            }

            return Convert.ToHexStringLower(SHA256.HashData(canonical.WrittenSpan));
        }
        // InvalidOperationException: a string escapes an unpaired surrogate, which has no UTF-16 value to rewrite.
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException)
        {
            // A payload no reader can parse has no canonical form. The caller treats it as differing from every
            // declared payload, so the seed overwrites it rather than failing.
            return null;
        }
    }

    private static void _WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    _WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    _WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            default:
                // A number keeps its written text, so no precision is lost or invented; true, false and null have one
                // spelling each.
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
        }
    }

    private static MemoryStream _OpenCompressedPayload(byte[] gzipBytes)
    {
        var payloadLength = gzipBytes.Length - _GZipSignature.Length;

        if (!_HasGZipSentinel(gzipBytes))
        {
            throw new InvalidOperationException("The bytes are not GZip compressed.");
        }

        // The sentinel is stored after the GZip member. Wrap only the member segment so typed reads can stream
        // directly from the persistence buffer without allocating a second byte array or an intermediate string.
        return new MemoryStream(gzipBytes, index: 0, count: payloadLength, writable: false, publiclyVisible: false);
    }

    private static bool _HasGZipSentinel(byte[] bytes)
    {
        return bytes.Length >= _GZipSignature.Length
            && bytes.AsSpan(bytes.Length - _GZipSignature.Length).SequenceEqual(_GZipSignature);
    }
}
