// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Binary;
using Headless.Checks;
using StackExchange.Redis;

namespace Headless.Caching;

internal static class RedisCacheEntryFrame
{
    // Fixed header layout (v3): [0] Magic, [1] Version, [2] flags, [3..10] logical, [11..18] physical,
    // [19..26] CreatedAt. The flags byte is full at 8 bits, so CreatedAt has no flag of its own: it is an
    // always-present fixed field in v3 (presence implied by version), with CreatedAtAbsentSentinel meaning
    // "no birth time recorded" (legacy/unframed source). Optional sections (sliding, eager, last-modified,
    // etag, tags) follow the fixed header at HeaderLength.
    public const int HeaderLength = 27;

    private const int _CreatedAtOffset = 19;

    // Sentinel written into the always-present CreatedAt slot when no birth time is known. long.MinValue is far
    // outside the valid Unix-millisecond range the codec accepts, so it can never collide with a real timestamp.
    internal const long CreatedAtAbsentSentinel = long.MinValue;

    internal const byte Magic = 0xFF;
    internal const byte Version = 0x03;
    internal const byte NullFlag = 1 << 0;
    internal const byte HasLogicalExpiresAtFlag = 1 << 1;
    internal const byte HasPhysicalExpiresAtFlag = 1 << 2;
    internal const byte HasSlidingExpirationFlag = 1 << 3;
    internal const byte HasEagerRefreshAtFlag = 1 << 4;
    internal const byte HasETagFlag = 1 << 5;
    internal const byte HasLastModifiedAtFlag = 1 << 6;
    internal const byte HasTagsFlag = 1 << 7;

    // Valid range accepted by DateTimeOffset.FromUnixTimeMilliseconds; out-of-range values mean the
    // header bytes were never written by this codec, so the frame must read as a miss rather than throw.
    internal const long MinUnixEpochMilliseconds = -62_135_596_800_000L; // 0001-01-01T00:00:00.000Z
    internal const long MaxUnixEpochMilliseconds = 253_402_300_799_999L; // 9999-12-31T23:59:59.999Z

    public static byte[] Encode(
        RedisValue value,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt,
        TimeSpan? slidingExpiration,
        DateTime? eagerRefreshAt = null,
        string? etag = null,
        DateTime? lastModifiedAt = null,
        IReadOnlyCollection<string>? tags = null,
        DateTime? createdAt = null
    )
    {
        var valueBytes = isNull ? [] : _ToBytes(value);

        var buffer = _BuildFrame(
            valueBytes.Length,
            isNull,
            logicalExpiresAt,
            physicalExpiresAt,
            slidingExpiration,
            eagerRefreshAt,
            etag,
            lastModifiedAt,
            tags,
            createdAt,
            out var payloadOffset
        );

        valueBytes.CopyTo(buffer.AsSpan(payloadOffset));

        return buffer;
    }

    /// <summary>
    /// Encodes a framed entry whose value payload comes from a <see cref="ReadOnlySequence{T}"/> instead of a
    /// contiguous <c>byte[]</c>. The header/section layout is identical to the <see cref="RedisValue"/> overload;
    /// only the payload boundary changes, so the zero-intermediate-copy buffer path produces byte-identical frames.
    /// The sequence is copied directly into the single frame buffer at the value offset (one copy, no intermediate
    /// materialization).
    /// </summary>
    public static byte[] Encode(
        ReadOnlySequence<byte> payload,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt,
        TimeSpan? slidingExpiration,
        DateTime? eagerRefreshAt = null,
        string? etag = null,
        DateTime? lastModifiedAt = null,
        IReadOnlyCollection<string>? tags = null,
        DateTime? createdAt = null
    )
    {
        // Frame length must index a byte[]: surface an oversized payload as a clear contract error rather than the
        // implicit OverflowException a checked cast would throw (Redis itself caps a value at 512 MB regardless).
        if (!isNull && payload.Length > Array.MaxLength)
        {
            throw new ArgumentException(
                $"Cache payload of {payload.Length} bytes exceeds the maximum supported size of {Array.MaxLength} bytes.",
                nameof(payload)
            );
        }

        var length = isNull ? 0 : (int)payload.Length;

        var buffer = _BuildFrame(
            length,
            isNull,
            logicalExpiresAt,
            physicalExpiresAt,
            slidingExpiration,
            eagerRefreshAt,
            etag,
            lastModifiedAt,
            tags,
            createdAt,
            out var payloadOffset
        );

        if (!isNull)
        {
            payload.CopyTo(buffer.AsSpan(payloadOffset));
        }

        return buffer;
    }

    /// <summary>
    /// Builds the framed buffer sized <c>payloadOffset + valueLength</c> with the full header and every present
    /// optional section written, leaving the value segment (from <paramref name="payloadOffset"/> to the end)
    /// uninitialized for the caller to fill. This is the payload-agnostic core shared by both <c>Encode</c>
    /// overloads, so the only difference between them is how the value bytes are copied into the tail.
    /// </summary>
    private static byte[] _BuildFrame(
        int valueLength,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt,
        TimeSpan? slidingExpiration,
        DateTime? eagerRefreshAt,
        string? etag,
        DateTime? lastModifiedAt,
        IReadOnlyCollection<string>? tags,
        DateTime? createdAt,
        out int payloadOffset
    )
    {
        var etagBytes = etag is null ? null : Encoding.UTF8.GetBytes(etag);
        if (etagBytes is not null)
        {
            Argument.IsLessThanOrEqualTo(etagBytes.Length, ushort.MaxValue, paramName: nameof(etag));
        }

        // An empty tag collection encodes as absent: there is no observable difference between "no tags"
        // and "zero tags", and omitting the section keeps the frame minimal.
        //
        // For the transient scratch buffer used while building the tag section we rent from ArrayPool —
        // it is fully consumed (CopyTo'd) inside this method and returned before we exit, so its lifetime
        // is strictly contained. The main frame buffer is NOT pooled: StackExchange.Redis stores the
        // RedisValue (and its backing byte[]) inside the queued Message object and reads it asynchronously
        // when the socket write fires, so the buffer escapes this method and returning it to the pool would
        // corrupt in-flight data.
        byte[]? tagPooled = null;
        int tagLength = 0;

        if (tags is { Count: > 0 })
        {
            tagLength = _MeasureTagsLength(tags);
            tagPooled = ArrayPool<byte>.Shared.Rent(tagLength);
        }

        try
        {
            payloadOffset = HeaderLength;
            var flags = isNull ? NullFlag : (byte)0;

            if (logicalExpiresAt.HasValue)
            {
                flags |= HasLogicalExpiresAtFlag;
            }

            if (physicalExpiresAt.HasValue)
            {
                flags |= HasPhysicalExpiresAtFlag;
            }

            if (slidingExpiration.HasValue)
            {
                flags |= HasSlidingExpirationFlag;
                payloadOffset += sizeof(long);
            }

            if (eagerRefreshAt.HasValue)
            {
                flags |= HasEagerRefreshAtFlag;
                payloadOffset += sizeof(long);
            }

            if (lastModifiedAt.HasValue)
            {
                flags |= HasLastModifiedAtFlag;
                payloadOffset += sizeof(long);
            }

            if (etagBytes is not null)
            {
                flags |= HasETagFlag;
                payloadOffset += sizeof(ushort) + etagBytes.Length;
            }

            if (tagPooled is not null)
            {
                // Write the tag section into the pooled scratch buffer (slice to exact logical length to
                // guard against the pool returning an oversized array).
                _WriteTagsInto(tags!, tagPooled.AsSpan(0, tagLength));
                flags |= HasTagsFlag;
                payloadOffset += tagLength;
            }

            var buffer = new byte[payloadOffset + valueLength];
            buffer[0] = Magic;
            buffer[1] = Version;
            buffer[2] = flags;

            if (logicalExpiresAt.HasValue)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    buffer.AsSpan(3, sizeof(long)),
                    _ToUnixTimeMilliseconds(logicalExpiresAt.Value)
                );
            }

            if (physicalExpiresAt.HasValue)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    buffer.AsSpan(11, sizeof(long)),
                    _ToUnixTimeMilliseconds(physicalExpiresAt.Value)
                );
            }

            // CreatedAt is an always-present v3 fixed field (no flag bit left). Write the birth time, or the
            // absent-sentinel when none is known, so decode can distinguish a real timestamp from "unset".
            BinaryPrimitives.WriteInt64LittleEndian(
                buffer.AsSpan(_CreatedAtOffset, sizeof(long)),
                createdAt.HasValue ? _ToUnixTimeMilliseconds(createdAt.Value) : CreatedAtAbsentSentinel
            );

            // Optional sections follow the fixed header in layout order: fixed-width fields first (sliding,
            // eager-refresh, last-modified), then the var-length sections (etag, tags), then the value segment.
            var offset = HeaderLength;

            if (slidingExpiration.HasValue)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    buffer.AsSpan(offset, sizeof(long)),
                    (long)slidingExpiration.Value.TotalMilliseconds
                );
                offset += sizeof(long);
            }

            if (eagerRefreshAt.HasValue)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    buffer.AsSpan(offset, sizeof(long)),
                    _ToUnixTimeMilliseconds(eagerRefreshAt.Value)
                );
                offset += sizeof(long);
            }

            if (lastModifiedAt.HasValue)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    buffer.AsSpan(offset, sizeof(long)),
                    _ToUnixTimeMilliseconds(lastModifiedAt.Value)
                );
                offset += sizeof(long);
            }

            if (etagBytes is not null)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    buffer.AsSpan(offset, sizeof(ushort)),
                    (ushort)etagBytes.Length
                );
                offset += sizeof(ushort);
                etagBytes.CopyTo(buffer.AsSpan(offset));
                offset += etagBytes.Length;
            }

            if (tagPooled is not null)
            {
                // Copy from the pooled scratch (exact logical length) into the frame buffer.
                tagPooled.AsSpan(0, tagLength).CopyTo(buffer.AsSpan(offset));
                offset += tagLength;
            }

            // The value segment (offset == payloadOffset here) is left for the caller to fill; both Encode
            // overloads copy their payload into buffer[payloadOffset..] after this method returns.
            return buffer;
        }
        finally
        {
            if (tagPooled is not null)
            {
                ArrayPool<byte>.Shared.Return(tagPooled);
            }
        }
    }

    public static DecodedFrame Decode(RedisValue value)
    {
        if (!value.HasValue)
        {
            return DecodedFrame.Unframed;
        }

        var bytes = _ToBytes(value);

        if (bytes.Length < HeaderLength || bytes[0] != Magic)
        {
            return DecodedFrame.Unframed;
        }

        // Any other version (including the retired v1) reads as unframed legacy bytes: the value segment
        // layout is unknown, so the safe interpretation is a decode miss rather than a partial parse.
        if (bytes[1] != Version)
        {
            return DecodedFrame.Unframed;
        }

        var flags = bytes[2];

        var hasLogical = (flags & HasLogicalExpiresAtFlag) is not 0;
        var hasPhysical = (flags & HasPhysicalExpiresAtFlag) is not 0;
        var hasSliding = (flags & HasSlidingExpirationFlag) is not 0;
        var hasEagerRefresh = (flags & HasEagerRefreshAtFlag) is not 0;
        var hasETag = (flags & HasETagFlag) is not 0;
        var hasLastModified = (flags & HasLastModifiedAtFlag) is not 0;
        var hasTags = (flags & HasTagsFlag) is not 0;

        var logicalMs = hasLogical ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(3, sizeof(long))) : 0L;
        var physicalMs = hasPhysical ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(11, sizeof(long))) : 0L;

        // CreatedAt occupies a fixed v3 slot in the header (always present, gated by version not a flag). The
        // HeaderLength guard above already proved these bytes exist. The absent-sentinel decodes back to null.
        var createdAtMs = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(_CreatedAtOffset, sizeof(long)));
        var hasCreatedAt = createdAtMs != CreatedAtAbsentSentinel;

        var offset = HeaderLength;

        if (!_TryReadInt64(bytes, hasSliding, ref offset, out var slidingMs))
        {
            return DecodedFrame.Unframed;
        }

        if (!_TryReadInt64(bytes, hasEagerRefresh, ref offset, out var eagerRefreshMs))
        {
            return DecodedFrame.Unframed;
        }

        if (!_TryReadInt64(bytes, hasLastModified, ref offset, out var lastModifiedMs))
        {
            return DecodedFrame.Unframed;
        }

        string? etag = null;

        if (hasETag && !_TryReadString(bytes, ref offset, out etag))
        {
            return DecodedFrame.Unframed;
        }

        string[]? decodedTags = null;

        if (hasTags && !_TryReadTags(bytes, ref offset, out decodedTags))
        {
            return DecodedFrame.Unframed;
        }

        if (
            (hasLogical && _IsOutOfRange(logicalMs))
            || (hasPhysical && _IsOutOfRange(physicalMs))
            || (hasSliding && slidingMs <= 0)
            || (hasEagerRefresh && _IsOutOfRange(eagerRefreshMs))
            || (hasLastModified && _IsOutOfRange(lastModifiedMs))
            || (hasCreatedAt && _IsOutOfRange(createdAtMs))
        )
        {
            return DecodedFrame.Unframed;
        }

        return new DecodedFrame(
            IsFramed: true,
            IsNull: (flags & NullFlag) is not 0,
            LogicalExpiresAt: hasLogical ? _FromUnixTimeMilliseconds(logicalMs) : null,
            PhysicalExpiresAt: hasPhysical ? _FromUnixTimeMilliseconds(physicalMs) : null,
            SlidingExpiration: hasSliding ? TimeSpan.FromMilliseconds(slidingMs) : null,
            EagerRefreshAt: hasEagerRefresh ? _FromUnixTimeMilliseconds(eagerRefreshMs) : null,
            ETag: etag,
            LastModifiedAt: hasLastModified ? _FromUnixTimeMilliseconds(lastModifiedMs) : null,
            Tags: decodedTags is { Length: > 0 } ? decodedTags : null,
            CreatedAt: hasCreatedAt ? _FromUnixTimeMilliseconds(createdAtMs) : null,
            ValueSegment: bytes.AsMemory(offset)
        );
    }

    /// <summary>
    /// Decodes only the metadata needed to re-arm a sliding entry (no value, no tags) from a fixed prefix of the
    /// frame: the 27-byte header plus the sliding field, which is the first optional section and therefore sits
    /// immediately after the header at <see cref="HeaderLength"/>. Used by the value-free <c>RefreshAsync</c> path
    /// so a large value payload is never transferred just to push out its TTL. Returns <see langword="false"/>
    /// (treat as a miss) when the prefix is not a recognizable framed header, or when a present sliding field is
    /// truncated by a too-short prefix.
    /// </summary>
    /// <param name="prefix">A leading slice of the stored value, at least <see cref="HeaderLength"/> bytes.</param>
    /// <param name="header">The decoded header view when this returns <see langword="true"/>.</param>
    public static bool TryDecodeHeader(ReadOnlySpan<byte> prefix, out HeaderView header)
    {
        header = default;

        if (prefix.Length < HeaderLength || prefix[0] != Magic || prefix[1] != Version)
        {
            return false;
        }

        var flags = prefix[2];
        var hasLogical = (flags & HasLogicalExpiresAtFlag) is not 0;
        var hasPhysical = (flags & HasPhysicalExpiresAtFlag) is not 0;
        var hasSliding = (flags & HasSlidingExpirationFlag) is not 0;
        var hasTags = (flags & HasTagsFlag) is not 0;

        var logicalMs = hasLogical ? BinaryPrimitives.ReadInt64LittleEndian(prefix.Slice(3, sizeof(long))) : 0L;
        var physicalMs = hasPhysical ? BinaryPrimitives.ReadInt64LittleEndian(prefix.Slice(11, sizeof(long))) : 0L;
        var createdAtMs = BinaryPrimitives.ReadInt64LittleEndian(prefix.Slice(_CreatedAtOffset, sizeof(long)));
        var hasCreatedAt = createdAtMs != CreatedAtAbsentSentinel;

        var slidingMs = 0L;

        if (hasSliding)
        {
            // Sliding is the first optional field, so it occupies the eight bytes right after the fixed header.
            if (prefix.Length < HeaderLength + sizeof(long))
            {
                return false;
            }

            slidingMs = BinaryPrimitives.ReadInt64LittleEndian(prefix.Slice(HeaderLength, sizeof(long)));
        }

        if (
            (hasLogical && _IsOutOfRange(logicalMs))
            || (hasPhysical && _IsOutOfRange(physicalMs))
            || (hasSliding && slidingMs <= 0)
            || (hasCreatedAt && _IsOutOfRange(createdAtMs))
        )
        {
            return false;
        }

        header = new HeaderView(
            HasTags: hasTags,
            LogicalExpiresAt: hasLogical ? _FromUnixTimeMilliseconds(logicalMs) : null,
            PhysicalExpiresAt: hasPhysical ? _FromUnixTimeMilliseconds(physicalMs) : null,
            SlidingExpiration: hasSliding ? TimeSpan.FromMilliseconds(slidingMs) : null,
            CreatedAt: hasCreatedAt ? _FromUnixTimeMilliseconds(createdAtMs) : null
        );

        return true;
    }

    /// <summary>
    /// Encodes a tag collection as the frame's tag section bytes: u16le count, then per tag a u16le UTF-8 byte
    /// length followed by the UTF-8 bytes. The tagged-write Lua script parses the same layout to fan the tags
    /// out into the reverse tag index, so this is the single tag wire encoding.
    /// </summary>
    internal static byte[] EncodeTags(IReadOnlyCollection<string> tags)
    {
        var length = _MeasureTagsLength(tags);
        var buffer = new byte[length];
        _WriteTagsInto(tags, buffer.AsSpan());

        return buffer;
    }

    /// <summary>
    /// Measures the exact number of bytes required to encode <paramref name="tags"/> in the tag-section wire
    /// format (u16le count + per-tag u16le-length-prefixed UTF-8). Used to size a buffer before writing.
    /// </summary>
    private static int _MeasureTagsLength(IReadOnlyCollection<string> tags)
    {
        Argument.IsLessThanOrEqualTo(tags.Count, ushort.MaxValue, paramName: nameof(tags));

        var length = sizeof(ushort); // u16le tag count prefix

        foreach (var tag in tags)
        {
            var tagByteCount = Encoding.UTF8.GetByteCount(tag);
            Argument.IsLessThanOrEqualTo(tagByteCount, ushort.MaxValue, paramName: nameof(tags));
            length += sizeof(ushort) + tagByteCount; // per-tag: u16le length prefix + UTF-8 bytes
        }

        return length;
    }

    /// <summary>
    /// Writes the tag-section wire encoding into <paramref name="destination"/>, which must be exactly
    /// <see cref="_MeasureTagsLength"/> bytes long (or a correctly-sized slice of a pooled buffer).
    /// </summary>
    private static void _WriteTagsInto(IReadOnlyCollection<string> tags, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination[..sizeof(ushort)], (ushort)tags.Count);
        var offset = sizeof(ushort);

        // Encode each tag directly into the pre-sized destination span.
        foreach (var tag in tags)
        {
            // Write the UTF-8 bytes into the buffer starting after the per-tag length prefix slot,
            // then back-fill the prefix with the actual byte count written.
            var written = Encoding.UTF8.GetBytes(tag, destination[(offset + sizeof(ushort))..]);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, sizeof(ushort)), (ushort)written);
            offset += sizeof(ushort) + written;
        }
    }

    private static bool _TryReadInt64(byte[] bytes, bool present, ref int offset, out long value)
    {
        value = 0L;

        if (!present)
        {
            return true;
        }

        if (bytes.Length < offset + sizeof(long))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, sizeof(long)));
        offset += sizeof(long);
        return true;
    }

    private static bool _TryReadString(byte[] bytes, ref int offset, out string? value)
    {
        value = null;

        if (bytes.Length < offset + sizeof(ushort))
        {
            return false;
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
        offset += sizeof(ushort);

        if (bytes.Length < offset + length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(bytes.AsSpan(offset, length));
        offset += length;
        return true;
    }

    private static bool _TryReadTags(byte[] bytes, ref int offset, out string[]? tags)
    {
        tags = null;

        if (bytes.Length < offset + sizeof(ushort))
        {
            return false;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
        offset += sizeof(ushort);

        var decoded = new string[count];

        for (var i = 0; i < count; i++)
        {
            if (!_TryReadString(bytes, ref offset, out var tag))
            {
                return false;
            }

            decoded[i] = tag!;
        }

        tags = decoded;
        return true;
    }

    private static byte[] _ToBytes(RedisValue value)
    {
        if (!value.HasValue)
        {
            return [];
        }

        return (byte[])value!;
    }

    /// <summary>Converts a (UTC) timestamp to the Unix-millisecond representation stored in the frame header.</summary>
    internal static long ToUnixTimeMilliseconds(DateTime value) => _ToUnixTimeMilliseconds(value);

    /// <summary>Converts a Unix-millisecond marker value back to its UTC timestamp.</summary>
    internal static DateTime FromUnixTimeMilliseconds(long value) => _FromUnixTimeMilliseconds(value);

    /// <summary>
    /// Parses an invalidation-marker Unix-millisecond string written by <c>RemoveByTagAsync</c>/<c>ClearAsync</c>
    /// into its Unix-ms long, or <see langword="null"/> when the value is absent or unparseable/out-of-range.
    /// </summary>
    internal static long? TryParseMarkerMs(RedisValue value)
    {
        if (
            !value.HasValue
            || !long.TryParse((string?)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            || _IsOutOfRange(ms)
        )
        {
            return null;
        }

        return ms;
    }

    private static long _ToUnixTimeMilliseconds(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    private static DateTime _FromUnixTimeMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

    private static bool _IsOutOfRange(long value) => value is < MinUnixEpochMilliseconds or > MaxUnixEpochMilliseconds;

    /// <summary>
    /// The subset of frame metadata recovered by <see cref="TryDecodeHeader"/> for the value-free re-arm path:
    /// expiration stamps, the sliding window, the birth time (for clear/remove marker checks), and whether the
    /// entry carries tags (which require the full frame for per-tag marker resolution).
    /// </summary>
    internal readonly record struct HeaderView(
        bool HasTags,
        DateTime? LogicalExpiresAt,
        DateTime? PhysicalExpiresAt,
        TimeSpan? SlidingExpiration,
        DateTime? CreatedAt
    );

    internal readonly record struct DecodedFrame(
        bool IsFramed,
        bool IsNull,
        DateTime? LogicalExpiresAt,
        DateTime? PhysicalExpiresAt,
        TimeSpan? SlidingExpiration,
        DateTime? EagerRefreshAt,
        string? ETag,
        DateTime? LastModifiedAt,
        IReadOnlyCollection<string>? Tags,
        DateTime? CreatedAt,
        ReadOnlyMemory<byte> ValueSegment
    )
    {
        public static DecodedFrame Unframed { get; } =
            new(
                IsFramed: false,
                IsNull: false,
                LogicalExpiresAt: null,
                PhysicalExpiresAt: null,
                SlidingExpiration: null,
                EagerRefreshAt: null,
                ETag: null,
                LastModifiedAt: null,
                Tags: null,
                CreatedAt: null,
                ValueSegment: ReadOnlyMemory<byte>.Empty
            );
    }
}
