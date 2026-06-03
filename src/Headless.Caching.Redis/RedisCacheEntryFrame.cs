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

    public static byte[] Encode(
        RedisValue value,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt
    )
    {
        var valueBytes = isNull ? [] : _ToBytes(value);
        var buffer = new byte[HeaderLength + valueBytes.Length];
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

        buffer[2] = flags;
        valueBytes.CopyTo(buffer.AsSpan(HeaderLength));

        return buffer;
    }

    public static DecodedFrame Decode(RedisValue value)
    {
        if (!value.HasValue)
        {
            return DecodedFrame.Unframed;
        }

        var bytes = _ToBytes(value);

        if (bytes.Length < HeaderLength || bytes[0] != Magic || bytes[1] != Version)
        {
            return DecodedFrame.Unframed;
        }

        var flags = bytes[2];
        var logicalExpiresAt = (flags & HasLogicalExpiresAtFlag) is 0
            ? (DateTime?)null
            : _FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(3, sizeof(long))));
        var physicalExpiresAt = (flags & HasPhysicalExpiresAtFlag) is 0
            ? (DateTime?)null
            : _FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(11, sizeof(long))));

        return new DecodedFrame(
            IsFramed: true,
            IsNull: (flags & NullFlag) is not 0,
            LogicalExpiresAt: logicalExpiresAt,
            PhysicalExpiresAt: physicalExpiresAt,
            ValueSegment: bytes.AsMemory(HeaderLength)
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

    internal readonly record struct DecodedFrame(
        bool IsFramed,
        bool IsNull,
        DateTime? LogicalExpiresAt,
        DateTime? PhysicalExpiresAt,
        ReadOnlyMemory<byte> ValueSegment
    )
    {
        public static DecodedFrame Unframed { get; } = new(
            IsFramed: false,
            IsNull: false,
            LogicalExpiresAt: null,
            PhysicalExpiresAt: null,
            ValueSegment: ReadOnlyMemory<byte>.Empty
        );
    }
}
