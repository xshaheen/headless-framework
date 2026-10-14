// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using Headless.Serializer;

namespace Headless.Idempotency;

/// <summary>
/// The captured response a completed request replays: status, allowlisted headers, and body bytes. Stored in the
/// durable idempotency record under <see cref="Contract" /> as a length-framed payload — the body bytes raw, never
/// base64 inside JSON, so a stored body occupies its exact size on the wire and in every store.
/// </summary>
/// <remarks>
/// <para>
/// The frame is <c>[body][header JSON][4-byte big-endian header length]</c>. The body comes first so captured bytes
/// can be written straight into the frame without a copy, and the header length rides at the end because it is only
/// known after the header is serialized. <see cref="Decode" /> rejects a payload whose trailing length disagrees
/// with its size instead of misreading it.
/// </para>
/// <para>
/// <see cref="Contract" /> changed from <c>headless.api.idempotency.response/v1</c> when this frame replaced the
/// JSON-with-base64 shape: a replay refuses bytes an older version wrote instead of misreading them.
/// </para>
/// </remarks>
internal sealed class IdempotencyResponseSnapshot
{
    public const string Contract = "headless.idempotency.response/v2";

    public int StatusCode { get; init; }

    /// <summary>
    /// Allowlisted response headers captured at completion. Replay filters them through the allowlist, whose lookups
    /// are case-insensitive, so this dictionary's own comparer does not matter.
    /// </summary>
    public Dictionary<string, string[]> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[] Body { get; init; } = [];

    /// <summary>Frames this snapshot into a fresh array. The middleware uses <see cref="WriteTo" /> instead, which
    /// keeps the frame in a pooled buffer and copies the body straight from the capture buffer.</summary>
    public byte[] Encode()
    {
        using var writer = new PooledByteBufferWriter(Body.Length + 256);
        WriteTo(writer, StatusCode, Headers, Body);

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Writes a captured response as a frame into <paramref name="writer" />: the body first, then the header JSON,
    /// then the header's byte length. No intermediate array holds the body.
    /// </summary>
    public static void WriteTo(
        IBufferWriter<byte> writer,
        int statusCode,
        Dictionary<string, string[]> headers,
        ReadOnlySpan<byte> body
    )
    {
        writer.Write(body);

        // Utf8JsonWriter keeps an internal buffer and only Flush commits it to the writer, so the header length is
        // read after the flush, never from BytesCommitted mid-write.
        using var json = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(
            json,
            new IdempotencyResponseSnapshotHeader { StatusCode = statusCode, Headers = headers },
            IdempotencyJsonContext.Default.IdempotencyResponseSnapshotHeader
        );
        json.Flush();

        BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(sizeof(int)), checked((int)json.BytesCommitted));
        writer.Advance(sizeof(int));
    }

    /// <summary>Reads a stored frame back: status, headers, and the body bytes.</summary>
    /// <exception cref="JsonException">The payload is not a frame this version wrote.</exception>
    public static IdempotencyResponseSnapshot Decode(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;

        if (span.Length < sizeof(int))
        {
            throw new JsonException("The stored idempotent response is shorter than its frame footer.");
        }

        var headerLength = BinaryPrimitives.ReadInt32BigEndian(span[^sizeof(int)..]);
        var bodyLength = span.Length - sizeof(int) - headerLength;

        if (headerLength < 2 || bodyLength < 0)
        {
            throw new JsonException("The stored idempotent response frame has an inconsistent header length.");
        }

        var header =
            JsonSerializer.Deserialize(
                span[bodyLength..^sizeof(int)],
                IdempotencyJsonContext.Default.IdempotencyResponseSnapshotHeader
            ) ?? throw new JsonException("The stored idempotent response header is null.");

        return new IdempotencyResponseSnapshot
        {
            StatusCode = header.StatusCode,
            Headers = header.Headers,
            Body = span[..bodyLength].ToArray(),
        };
    }
}

/// <summary>The JSON header of a stored response frame: everything but the body, which the frame carries raw.</summary>
internal sealed class IdempotencyResponseSnapshotHeader
{
    public int StatusCode { get; set; }

    public Dictionary<string, string[]> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSerializable(typeof(IdempotencyResponseSnapshotHeader))]
internal sealed partial class IdempotencyJsonContext : JsonSerializerContext;
