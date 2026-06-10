// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using StackExchange.Redis;

namespace Headless.Caching;

internal static class RedisCacheEntryFrame
{
    public const int HeaderLength = 19;

    internal const byte Magic = 0xFF;
    internal const byte Version = 0x01;
    internal const byte NullFlag = 1 << 0;
    internal const byte HasLogicalExpiresAtFlag = 1 << 1;
    internal const byte HasPhysicalExpiresAtFlag = 1 << 2;
    internal const byte HasSlidingExpirationFlag = 1 << 3;

    // Valid range accepted by DateTimeOffset.FromUnixTimeMilliseconds; out-of-range values mean the
    // header bytes were never written by this codec, so the frame must read as a miss rather than throw.
    internal const long MinUnixEpochMilliseconds = -62_135_596_800_000L; // 0001-01-01T00:00:00.000Z
    internal const long MaxUnixEpochMilliseconds = 253_402_300_799_999L; // 9999-12-31T23:59:59.999Z

    public static byte[] Encode(
        RedisValue value,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt,
        TimeSpan? slidingExpiration
    )
    {
        var valueBytes = isNull ? [] : _ToBytes(value);
        var hasSlidingExpiration = slidingExpiration.HasValue;
        var payloadOffset = hasSlidingExpiration ? HeaderLength + sizeof(long) : HeaderLength;
        var buffer = new byte[payloadOffset + valueBytes.Length];
        buffer[0] = Magic;
        buffer[1] = Version;

        var flags = isNull ? NullFlag : (byte)0;

        if (logicalExpiresAt.HasValue)
        {
            flags |= HasLogicalExpiresAtFlag;
            BinaryPrimitives.WriteInt64LittleEndian(
                buffer.AsSpan(3, sizeof(long)),
                _ToUnixTimeMilliseconds(logicalExpiresAt.Value)
            );
        }

        if (physicalExpiresAt.HasValue)
        {
            flags |= HasPhysicalExpiresAtFlag;
            BinaryPrimitives.WriteInt64LittleEndian(
                buffer.AsSpan(11, sizeof(long)),
                _ToUnixTimeMilliseconds(physicalExpiresAt.Value)
            );
        }

        if (hasSlidingExpiration)
        {
            flags |= HasSlidingExpirationFlag;
            var slidingExpirationValue = slidingExpiration.GetValueOrDefault();
            BinaryPrimitives.WriteInt64LittleEndian(
                buffer.AsSpan(HeaderLength, sizeof(long)),
                (long)slidingExpirationValue.TotalMilliseconds
            );
        }

        buffer[2] = flags;
        valueBytes.CopyTo(buffer.AsSpan(payloadOffset));

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

        // The magic byte marks a frame this codec wrote; an unrecognized version is a real corruption
        // or a forward-incompatible writer, so fail loud instead of silently treating it as a raw value.
        if (bytes[1] != Version)
        {
            throw new NotSupportedException($"Unsupported cache frame version 0x{bytes[1]:X2}");
        }

        var flags = bytes[2];

        var hasLogical = (flags & HasLogicalExpiresAtFlag) is not 0;
        var hasPhysical = (flags & HasPhysicalExpiresAtFlag) is not 0;
        var hasSliding = (flags & HasSlidingExpirationFlag) is not 0;
        var payloadOffset = hasSliding ? HeaderLength + sizeof(long) : HeaderLength;

        if (bytes.Length < payloadOffset)
        {
            return DecodedFrame.Unframed;
        }

        var logicalMs = hasLogical ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(3, sizeof(long))) : 0L;
        var physicalMs = hasPhysical ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(11, sizeof(long))) : 0L;
        var slidingMs = hasSliding
            ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(HeaderLength, sizeof(long)))
            : 0L;

        if (
            (hasLogical && _IsOutOfRange(logicalMs))
            || (hasPhysical && _IsOutOfRange(physicalMs))
            || (hasSliding && slidingMs <= 0)
        )
        {
            return DecodedFrame.Unframed;
        }

        var logicalExpiresAt = hasLogical ? _FromUnixTimeMilliseconds(logicalMs) : (DateTime?)null;
        var physicalExpiresAt = hasPhysical ? _FromUnixTimeMilliseconds(physicalMs) : (DateTime?)null;
        var slidingExpiration = hasSliding ? TimeSpan.FromMilliseconds(slidingMs) : (TimeSpan?)null;

        return new DecodedFrame(
            IsFramed: true,
            IsNull: (flags & NullFlag) is not 0,
            LogicalExpiresAt: logicalExpiresAt,
            PhysicalExpiresAt: physicalExpiresAt,
            SlidingExpiration: slidingExpiration,
            ValueSegment: bytes.AsMemory(payloadOffset)
        );
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
                ValueSegment: ReadOnlyMemory<byte>.Empty
            );
    }
}
