// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Headless.Serializer;

namespace Headless.Idempotency.Caching;

/// <summary>
/// One idempotency record as the cache stores it: the whole record in one entry, so a single compare-and-swap changes
/// all of it or none of it.
/// </summary>
/// <remarks>
/// <para>
/// The entry is stored as a length-framed <c>byte[]</c> — <c>[header JSON][result][recovery state][8-byte footer]</c>, the
/// footer holding the big-endian lengths of the two byte segments, with <c>-1</c> for a null one —
/// so the result payload (a captured response body) is cached raw instead of base64 inside JSON text, and a cache
/// that stores <c>byte[]</c> natively holds it at its exact size. Every write compares against the exact bytes a
/// call read, never a re-serialization of them, so the comparison does not depend on encoder determinism. Instants
/// are the application clock's, in UTC. <see cref="HighestGeneration" /> is the last generation ever admitted on the
/// record, kept through a release (which clears <see cref="Generation" />), so a new admission can stay above it
/// even when the store's generation counter was evicted.
/// </para>
/// </remarks>
internal sealed record CacheIdempotencyEntry(
    IdempotencyRecordStatus Status,
    string FingerprintAlgorithm,
    byte[] Fingerprint,
    long? Generation,
    DateTimeOffset? LeaseExpiresAt,
    byte[]? Result,
    string? ResultContract,
    DateTimeOffset RetentionUntil,
    string? RecoveryPoint,
    byte[]? RecoveryState,
    string? RecoveryContract,
    long? HighestGeneration
)
{
    public byte[] Encode()
    {
        using var writer = new PooledByteBufferWriter(512 + (Result?.Length ?? 0) + (RecoveryState?.Length ?? 0));

        // Utf8JsonWriter keeps an internal buffer and only Flush commits it to the writer.
        using (var json = new Utf8JsonWriter(writer))
        {
            JsonSerializer.Serialize(
                json,
                new CacheIdempotencyEntryHeader(this),
                CacheIdempotencyJsonContext.Default.CacheIdempotencyEntryHeader
            );
            json.Flush();
        }

        writer.Write(Result);
        writer.Write(RecoveryState);
        BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(sizeof(int)), Result?.Length ?? -1);
        writer.Advance(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(sizeof(int)), RecoveryState?.Length ?? -1);
        writer.Advance(sizeof(int));

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Reads the record stored as <paramref name="payload" /> at the cache key <paramref name="key" />.</summary>
    /// <exception cref="InvalidOperationException">The bytes are not an idempotency record.</exception>
    public static CacheIdempotencyEntry Deserialize(string key, byte[] payload)
    {
        CacheIdempotencyEntryHeader header;
        byte[]? result;
        byte[]? recoveryState;

        try
        {
            var span = payload.AsSpan();

            if (span.Length < 2 * sizeof(int))
            {
                throw new JsonException("The entry is shorter than its frame footer.");
            }

            var resultLength = BinaryPrimitives.ReadInt32BigEndian(span[^(2 * sizeof(int))..^sizeof(int)]);
            var recoveryLength = BinaryPrimitives.ReadInt32BigEndian(span[^sizeof(int)..]);

            if (resultLength < -1 || recoveryLength < -1)
            {
                throw new JsonException("The entry's frame footer has a negative segment length.");
            }

            // Summed in long: two attacker-chosen footer lengths can overflow int and wrap the span arithmetic.
            var blobsLength = (long)(resultLength < 0 ? 0 : resultLength) + (recoveryLength < 0 ? 0 : recoveryLength);
            var headerLength = span.Length - blobsLength - (2 * sizeof(int));

            if (headerLength is <= 0 or > int.MaxValue)
            {
                throw new JsonException("The entry's frame has no header.");
            }

            var offset = (int)headerLength;

            header =
                JsonSerializer.Deserialize(
                    span[..offset],
                    CacheIdempotencyJsonContext.Default.CacheIdempotencyEntryHeader
                ) ?? throw new JsonException("The entry header is JSON null.");

            result = _ReadSegment(span, ref offset, resultLength);
            recoveryState = _ReadSegment(span, ref offset, recoveryLength);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"The cache entry '{key}' is not an idempotency record; another writer uses this key prefix.",
                e
            );
        }

        return new CacheIdempotencyEntry(
            header.Status,
            header.FingerprintAlgorithm,
            header.Fingerprint,
            header.Generation,
            header.LeaseExpiresAt,
            result,
            header.ResultContract,
            header.RetentionUntil,
            header.RecoveryPoint,
            recoveryState,
            header.RecoveryContract,
            header.HighestGeneration
        );
    }

    private static byte[]? _ReadSegment(ReadOnlySpan<byte> span, ref int offset, int length)
    {
        if (length < 0)
        {
            return null;
        }

        var segment = span.Slice(offset, length).ToArray();
        offset += length;

        return segment;
    }
}

/// <summary>The JSON header of a stored record frame: every field except the two byte segments the frame carries raw.</summary>
internal sealed record CacheIdempotencyEntryHeader(
    IdempotencyRecordStatus Status,
    string FingerprintAlgorithm,
    byte[] Fingerprint,
    long? Generation,
    DateTimeOffset? LeaseExpiresAt,
    string? ResultContract,
    DateTimeOffset RetentionUntil,
    string? RecoveryPoint,
    string? RecoveryContract,
    long? HighestGeneration
)
{
    public CacheIdempotencyEntryHeader()
        : this(
            Status: IdempotencyRecordStatus.Pending,
            FingerprintAlgorithm: string.Empty,
            Fingerprint: [],
            Generation: null,
            LeaseExpiresAt: null,
            ResultContract: null,
            RetentionUntil: DateTimeOffset.MinValue,
            RecoveryPoint: null,
            RecoveryContract: null,
            HighestGeneration: null
        ) { }

    public CacheIdempotencyEntryHeader(CacheIdempotencyEntry entry)
        : this(
            entry.Status,
            entry.FingerprintAlgorithm,
            entry.Fingerprint,
            entry.Generation,
            entry.LeaseExpiresAt,
            entry.ResultContract,
            entry.RetentionUntil,
            entry.RecoveryPoint,
            entry.RecoveryContract,
            entry.HighestGeneration
        ) { }
}

// Absent fields are omitted rather than written as null: a null byte array would otherwise read back as an empty
// segment instead of no segment.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CacheIdempotencyEntryHeader))]
internal sealed partial class CacheIdempotencyJsonContext : JsonSerializerContext;
