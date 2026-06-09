// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisCacheEntryFrameTests
{
    private const int _HeaderLength = 19;
    private const int _SlidingHeaderLength = 27;
    private const byte _HasSlidingExpirationFlag = 1 << 3;
    private static readonly Type _FrameType = Type.GetType(
        "Headless.Caching.RedisCacheEntryFrame, Headless.Caching.Redis",
        throwOnError: true
    )!;

    private static readonly MethodInfo _EncodeMethod = _FrameType.GetMethod(
        "Encode",
        BindingFlags.Public | BindingFlags.Static
    )!;

    private static readonly MethodInfo _DecodeMethod = _FrameType.GetMethod(
        "Decode",
        BindingFlags.Public | BindingFlags.Static
    )!;

    [Fact]
    public void should_round_trip_value_and_expiration_metadata()
    {
        var value = _RedisValue(Encoding.UTF8.GetBytes("value"));
        var logical = new DateTime(2026, 06, 03, 12, 00, 00, DateTimeKind.Utc);
        var physical = logical.AddMinutes(5);

        var encoded = _Encode(value, isNull: false, logical, physical);
        var decoded = _Decode(encoded);

        decoded.IsFramed.Should().BeTrue();
        decoded.IsNull.Should().BeFalse();
        decoded.LogicalExpiresAt.Should().Be(logical);
        decoded.PhysicalExpiresAt.Should().Be(physical);
        decoded.SlidingExpiration.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_encode_null_as_flag_with_empty_value_segment()
    {
        var encoded = _Encode(RedisValue.EmptyString, isNull: true, logicalExpiresAt: null, physicalExpiresAt: null);
        var decoded = _Decode(encoded);

        decoded.IsFramed.Should().BeTrue();
        decoded.IsNull.Should().BeTrue();
        decoded.ValueSegment.Length.Should().Be(0);
    }

    [Fact]
    public void should_round_trip_no_expiry_without_expiration_flags()
    {
        var encoded = _Encode("value", isNull: false, logicalExpiresAt: null, physicalExpiresAt: null);
        var decoded = _Decode(encoded);

        encoded[2].Should().Be(0);
        decoded.LogicalExpiresAt.Should().BeNull();
        decoded.PhysicalExpiresAt.Should().BeNull();
        decoded.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public void should_round_trip_sliding_expiration_with_flag_conditional_payload_offset()
    {
        var value = _RedisValue(Encoding.UTF8.GetBytes("value"));
        var logical = new DateTime(2026, 06, 03, 12, 00, 00, DateTimeKind.Utc);
        var physical = logical.AddMinutes(5);
        var sliding = TimeSpan.FromSeconds(30);

        var encoded = _Encode(value, isNull: false, logical, physical, sliding);
        var decoded = _Decode(encoded);

        (encoded[2] & _HasSlidingExpirationFlag).Should().Be(_HasSlidingExpirationFlag);
        BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(_HeaderLength, sizeof(long)))
            .Should()
            .Be((long)sliding.TotalMilliseconds);
        decoded.SlidingExpiration.Should().Be(sliding);
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
        encoded.AsSpan(_SlidingHeaderLength).ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_keep_non_sliding_frame_at_old_header_length()
    {
        var logical = new DateTime(2026, 06, 03, 12, 00, 00, DateTimeKind.Utc);
        var physical = logical.AddMinutes(5);

        var encoded = _Encode("value", isNull: false, logical, physical, slidingExpiration: null);
        var decoded = _Decode(encoded);

        (encoded[2] & _HasSlidingExpirationFlag).Should().Be(0);
        encoded.Length.Should().Be(_HeaderLength + Encoding.UTF8.GetByteCount("value"));
        encoded.AsSpan(_HeaderLength).ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
        decoded.SlidingExpiration.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_decode_old_layout_payload_from_header_length_nineteen()
    {
        var encoded = new byte[_HeaderLength + 5];
        encoded[0] = 0xFF;
        encoded[1] = 0x01;
        Encoding.UTF8.GetBytes("value").CopyTo(encoded.AsSpan(_HeaderLength));

        var decoded = _Decode(_RedisValue(encoded));

        decoded.IsFramed.Should().BeTrue();
        decoded.SlidingExpiration.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0xFF, 0x02, 0x00, 0x00 })]
    public void should_discriminate_unframed_values(byte[] value)
    {
        var decoded = _Decode(_RedisValue(value));

        decoded.IsFramed.Should().BeFalse();
    }

    [Fact]
    public void should_round_trip_empty_non_null_value_distinctly_from_null()
    {
        var encoded = _Encode(RedisValue.EmptyString, isNull: false, logicalExpiresAt: null, physicalExpiresAt: null);
        var decoded = _Decode(encoded);

        decoded.IsFramed.Should().BeTrue();
        decoded.IsNull.Should().BeFalse();
        decoded.ValueSegment.Length.Should().Be(0);
    }

    [Fact]
    public void should_encode_expiration_as_little_endian_unix_milliseconds()
    {
        var instant = new DateTime(2026, 06, 03, 12, 34, 56, 789, DateTimeKind.Utc);
        var expected = new DateTimeOffset(instant).ToUnixTimeMilliseconds();

        var encoded = _Encode("value", isNull: false, instant, physicalExpiresAt: null);

        BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(3, sizeof(long))).Should().Be(expected);
    }

    [Fact]
    public void should_encode_physical_expiration_as_little_endian_unix_milliseconds()
    {
        var logical = new DateTime(2026, 06, 03, 12, 34, 56, 789, DateTimeKind.Utc);
        var physical = logical.AddMinutes(7);
        var expectedPhysical = new DateTimeOffset(physical).ToUnixTimeMilliseconds();

        var encoded = _Encode("value", isNull: false, logical, physical);
        var decoded = _Decode(encoded);

        BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(11, sizeof(long))).Should().Be(expectedPhysical);
        decoded.PhysicalExpiresAt.Should().Be(physical);
    }

    [Fact]
    public void should_throw_for_unsupported_frame_version()
    {
        var bytes = new byte[_HeaderLength];
        bytes[0] = 0xFF;
        bytes[1] = 0x02;

        var act = () => _Decode(_RedisValue(bytes));

        act.Should().Throw<TargetInvocationException>().WithInnerException<NotSupportedException>();
    }

    private static byte[] _Encode(
        RedisValue value,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt,
        TimeSpan? slidingExpiration = null
    ) => (byte[])_EncodeMethod.Invoke(null, [value, isNull, logicalExpiresAt, physicalExpiresAt, slidingExpiration])!;

    private static RedisValue _RedisValue(byte[] value) => value;

    private static DecodedFrame _Decode(RedisValue value)
    {
        var decoded = _DecodeMethod.Invoke(null, [value])!;
        var type = decoded.GetType();

        return new DecodedFrame(
            (bool)type.GetProperty("IsFramed")!.GetValue(decoded)!,
            (bool)type.GetProperty("IsNull")!.GetValue(decoded)!,
            (DateTime?)type.GetProperty("LogicalExpiresAt")!.GetValue(decoded),
            (DateTime?)type.GetProperty("PhysicalExpiresAt")!.GetValue(decoded),
            (TimeSpan?)type.GetProperty("SlidingExpiration")!.GetValue(decoded),
            (ReadOnlyMemory<byte>)type.GetProperty("ValueSegment")!.GetValue(decoded)!
        );
    }

    private readonly record struct DecodedFrame(
        bool IsFramed,
        bool IsNull,
        DateTime? LogicalExpiresAt,
        DateTime? PhysicalExpiresAt,
        TimeSpan? SlidingExpiration,
        ReadOnlyMemory<byte> ValueSegment
    );
}
