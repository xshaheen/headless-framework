// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Text;
using Headless.Checks;
using StackExchange.Redis;

namespace Headless.Caching;

internal static class RedisCacheEntryFrame
{
    public const int HeaderLength = 19;

    internal const byte Magic = 0xFF;
    internal const byte Version = 0x02;
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
        IReadOnlyCollection<string>? tags = null
    )
    {
        var valueBytes = isNull ? [] : _ToBytes(value);
        var etagBytes = etag is null ? null : Encoding.UTF8.GetBytes(etag);
        // An empty tag collection encodes as absent: there is no observable difference between "no tags"
        // and "zero tags", and omitting the section keeps the frame minimal.
        var tagBytes = tags is { Count: > 0 } ? _EncodeTags(tags) : null;

        if (etagBytes is not null)
        {
            Argument.IsLessThanOrEqualTo(etagBytes.Length, ushort.MaxValue, paramName: nameof(etag));
        }

        var payloadOffset = HeaderLength;
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

        if (tagBytes is not null)
        {
            flags |= HasTagsFlag;
            payloadOffset += tagBytes.Length;
        }

        var buffer = new byte[payloadOffset + valueBytes.Length];
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
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, sizeof(ushort)), (ushort)etagBytes.Length);
            offset += sizeof(ushort);
            etagBytes.CopyTo(buffer.AsSpan(offset));
            offset += etagBytes.Length;
        }

        if (tagBytes is not null)
        {
            tagBytes.CopyTo(buffer.AsSpan(offset));
            offset += tagBytes.Length;
        }

        valueBytes.CopyTo(buffer.AsSpan(offset));

        return buffer;
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
            ValueSegment: bytes.AsMemory(offset)
        );
    }

    private static byte[] _EncodeTags(IReadOnlyCollection<string> tags)
    {
        Argument.IsLessThanOrEqualTo(tags.Count, ushort.MaxValue, paramName: nameof(tags));

        var encodedTags = new byte[tags.Count][];
        var length = sizeof(ushort);
        var index = 0;

        foreach (var tag in tags)
        {
            var tagValueBytes = Encoding.UTF8.GetBytes(tag);
            Argument.IsLessThanOrEqualTo(tagValueBytes.Length, ushort.MaxValue, paramName: nameof(tags));
            encodedTags[index++] = tagValueBytes;
            length += sizeof(ushort) + tagValueBytes.Length;
        }

        var buffer = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, sizeof(ushort)), (ushort)tags.Count);
        var offset = sizeof(ushort);

        foreach (var tagValueBytes in encodedTags)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.AsSpan(offset, sizeof(ushort)),
                (ushort)tagValueBytes.Length
            );
            offset += sizeof(ushort);
            tagValueBytes.CopyTo(buffer.AsSpan(offset));
            offset += tagValueBytes.Length;
        }

        return buffer;
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
                ValueSegment: ReadOnlyMemory<byte>.Empty
            );
    }
}
