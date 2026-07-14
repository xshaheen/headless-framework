// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Binary;
using Headless.Caching;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisCacheEntryFrameTests
{
    private const int _HeaderLength = 27;
    private const int _SlidingHeaderLength = 35;
    private const byte _HasSlidingExpirationFlag = 1 << 3;
    private const byte _HasEagerRefreshAtFlag = 1 << 4;
    private const byte _HasETagFlag = 1 << 5;
    private const byte _HasLastModifiedAtFlag = 1 << 6;
    private const byte _HasTagsFlag = 1 << 7;

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
        decoded.EagerRefreshAt.Should().BeNull();
        decoded.ETag.Should().BeNull();
        decoded.LastModifiedAt.Should().BeNull();
        decoded.Tags.Should().BeNull();
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
        BinaryPrimitives
            .ReadInt64LittleEndian(encoded.AsSpan(_HeaderLength, sizeof(long)))
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
        encoded.Should().HaveCount(_HeaderLength + Encoding.UTF8.GetByteCount("value"));
        encoded.AsSpan(_HeaderLength).ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
        decoded.SlidingExpiration.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_round_trip_eager_refresh_at_alone()
    {
        var eagerRefreshAt = new DateTime(2026, 06, 03, 12, 30, 00, DateTimeKind.Utc);

        var encoded = _Encode(
            "value",
            isNull: false,
            logicalExpiresAt: null,
            physicalExpiresAt: null,
            eagerRefreshAt: eagerRefreshAt
        );
        var decoded = _Decode(encoded);

        (encoded[2] & _HasEagerRefreshAtFlag).Should().Be(_HasEagerRefreshAtFlag);
        decoded.EagerRefreshAt.Should().Be(eagerRefreshAt);
        decoded.ETag.Should().BeNull();
        decoded.LastModifiedAt.Should().BeNull();
        decoded.Tags.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_round_trip_etag_alone()
    {
        var encoded = _Encode(
            "value",
            isNull: false,
            logicalExpiresAt: null,
            physicalExpiresAt: null,
            etag: "W/\"42\""
        );
        var decoded = _Decode(encoded);

        (encoded[2] & _HasETagFlag).Should().Be(_HasETagFlag);
        decoded.ETag.Should().Be("W/\"42\"");
        decoded.EagerRefreshAt.Should().BeNull();
        decoded.Tags.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_round_trip_created_at_in_fixed_v3_slot()
    {
        var createdAt = new DateTime(2026, 06, 02, 09, 45, 10, 500, DateTimeKind.Utc);

        var encoded = _Encode(
            "value",
            isNull: false,
            logicalExpiresAt: null,
            physicalExpiresAt: null,
            createdAt: createdAt
        );
        var decoded = _Decode(encoded);

        // CreatedAt is an always-present v3 fixed-header field at offset 19 (no flag bit).
        BinaryPrimitives
            .ReadInt64LittleEndian(encoded.AsSpan(19, sizeof(long)))
            .Should()
            .Be(new DateTimeOffset(createdAt).ToUnixTimeMilliseconds());
        decoded.CreatedAt.Should().Be(createdAt);
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_decode_absent_created_at_as_null()
    {
        var encoded = _Encode("value", isNull: false, logicalExpiresAt: null, physicalExpiresAt: null);
        var decoded = _Decode(encoded);

        decoded.CreatedAt.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_round_trip_last_modified_at_alone()
    {
        var lastModifiedAt = new DateTime(2026, 06, 01, 08, 15, 30, 123, DateTimeKind.Utc);

        var encoded = _Encode(
            "value",
            isNull: false,
            logicalExpiresAt: null,
            physicalExpiresAt: null,
            lastModifiedAt: lastModifiedAt
        );
        var decoded = _Decode(encoded);

        (encoded[2] & _HasLastModifiedAtFlag).Should().Be(_HasLastModifiedAtFlag);
        decoded.LastModifiedAt.Should().Be(lastModifiedAt);
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_round_trip_tags_alone()
    {
        var tags = new[] { "tenant:1", "products", "region-eu" };

        var encoded = _Encode("value", isNull: false, logicalExpiresAt: null, physicalExpiresAt: null, tags: tags);
        var decoded = _Decode(encoded);

        (encoded[2] & _HasTagsFlag).Should().Be(_HasTagsFlag);
        decoded.Tags.Should().Equal(tags);
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_round_trip_all_metadata_fields_together()
    {
        var logical = new DateTime(2026, 06, 03, 12, 00, 00, DateTimeKind.Utc);
        var physical = logical.AddMinutes(10);
        var sliding = TimeSpan.FromSeconds(45);
        var eagerRefreshAt = logical.AddMinutes(-1);
        var lastModifiedAt = logical.AddHours(-2);
        var tags = new[] { "a", "b" };

        var encoded = _Encode(
            "value",
            isNull: false,
            logical,
            physical,
            sliding,
            eagerRefreshAt,
            etag: "etag-1",
            lastModifiedAt,
            tags
        );
        var decoded = _Decode(encoded);

        decoded.IsFramed.Should().BeTrue();
        decoded.LogicalExpiresAt.Should().Be(logical);
        decoded.PhysicalExpiresAt.Should().Be(physical);
        decoded.SlidingExpiration.Should().Be(sliding);
        decoded.EagerRefreshAt.Should().Be(eagerRefreshAt);
        decoded.ETag.Should().Be("etag-1");
        decoded.LastModifiedAt.Should().Be(lastModifiedAt);
        decoded.Tags.Should().Equal(tags);
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_encode_empty_tags_collection_as_absent()
    {
        var encoded = _Encode("value", isNull: false, logicalExpiresAt: null, physicalExpiresAt: null, tags: []);
        var decoded = _Decode(encoded);

        (encoded[2] & _HasTagsFlag).Should().Be(0);
        encoded.Should().HaveCount(_HeaderLength + Encoding.UTF8.GetByteCount("value"));
        decoded.Tags.Should().BeNull();
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_round_trip_unicode_etag_and_tags()
    {
        const string etag = "وسم-έκδοση-🏷️";
        var tags = new[] { "منتجات", "πελάτες", "🌍-global" };

        var encoded = _Encode(
            "value",
            isNull: false,
            logicalExpiresAt: null,
            physicalExpiresAt: null,
            etag: etag,
            tags: tags
        );
        var decoded = _Decode(encoded);

        decoded.ETag.Should().Be(etag);
        decoded.Tags.Should().Equal(tags);
        decoded.ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
    }

    [Fact]
    public void should_decode_truncated_etag_section_as_unframed_miss()
    {
        var encoded = _Encode("value", isNull: false, logicalExpiresAt: null, physicalExpiresAt: null, etag: "etag-1");

        // Cut into the etag bytes: the declared u16 length now exceeds the remaining payload.
        var truncated = encoded.AsSpan(0, _HeaderLength + sizeof(ushort) + 2).ToArray();
        var decoded = _Decode(_RedisValue(truncated));

        decoded.IsFramed.Should().BeFalse();
    }

    [Fact]
    public void should_decode_truncated_tags_section_as_unframed_miss()
    {
        var encoded = _Encode(
            "value",
            isNull: false,
            logicalExpiresAt: null,
            physicalExpiresAt: null,
            tags: ["alpha", "beta"]
        );

        // Cut inside the first tag's bytes so the per-tag length prefix overruns the buffer.
        var truncated = encoded.AsSpan(0, _HeaderLength + sizeof(ushort) + sizeof(ushort) + 2).ToArray();
        var decoded = _Decode(_RedisValue(truncated));

        decoded.IsFramed.Should().BeFalse();
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
    public void should_emit_version_three_frames()
    {
        var encoded = _Encode("value", isNull: false, logicalExpiresAt: null, physicalExpiresAt: null);

        encoded[0].Should().Be(0xFF);
        encoded[1].Should().Be(0x03);
    }

    [Fact]
    public void should_decode_version_one_payload_as_unframed_miss()
    {
        // A retired v1 frame (magic intact, old version byte) must read as unframed legacy bytes, not throw.
        var bytes = new byte[_HeaderLength + 5];
        bytes[0] = 0xFF;
        bytes[1] = 0x01;
        Encoding.UTF8.GetBytes("value").CopyTo(bytes.AsSpan(_HeaderLength));

        var decoded = _Decode(_RedisValue(bytes));

        decoded.IsFramed.Should().BeFalse();
    }

    [Fact]
    public void should_decode_unknown_future_version_as_unframed_miss()
    {
        var bytes = new byte[_HeaderLength];
        bytes[0] = 0xFF;
        bytes[1] = 0x04;

        var decoded = _Decode(_RedisValue(bytes));

        decoded.IsFramed.Should().BeFalse();
    }

    [Fact]
    public void should_encode_spliced_frames_byte_identical_to_single_buffer_encode()
    {
        // EncodeSpliced feeds the wire directly (two-segment RedisValue, #580); the CAS scripts slice these
        // exact bytes, so the spliced encoding must be byte-identical to the single-buffer Encode output.
        var payload = Encoding.UTF8.GetBytes("spliced-payload");
        var logical = new DateTime(2026, 06, 03, 12, 00, 00, DateTimeKind.Utc);
        var physical = logical.AddMinutes(5);
        var eager = logical.AddMinutes(2);
        var lastModified = logical.AddMinutes(-3);
        var created = logical.AddMinutes(-10);
        string[] tags = ["tenant:1", "profile"];

        var single = _Encode(
            _RedisValue(payload),
            isNull: false,
            logical,
            physical,
            TimeSpan.FromSeconds(30),
            eager,
            etag: "etag-value",
            lastModified,
            tags,
            created
        );

        var spliced = RedisCacheEntryFrame.EncodeSpliced(
            payload,
            logical,
            physical,
            TimeSpan.FromSeconds(30),
            eager,
            etag: "etag-value",
            lastModified,
            tags,
            created
        );

        spliced.ToArray().Should().Equal(single);
    }

    [Fact]
    public void should_encode_spliced_minimal_frame_byte_identical_to_single_buffer_encode()
    {
        var payload = Encoding.UTF8.GetBytes("v");

        var single = _Encode(_RedisValue(payload), isNull: false, logicalExpiresAt: null, physicalExpiresAt: null);
        var spliced = RedisCacheEntryFrame.EncodeSpliced(
            payload,
            logicalExpiresAt: null,
            physicalExpiresAt: null,
            slidingExpiration: null
        );

        spliced.ToArray().Should().Equal(single);
    }

    [Fact]
    public void should_decode_frames_from_contiguous_memory()
    {
        // The lease read path (#580) decodes straight off pooled memory; the ValueSegment must be a slice of
        // the supplied buffer with the same content the RedisValue decode path produces.
        var value = Encoding.UTF8.GetBytes("memory-decoded");
        var logical = new DateTime(2026, 06, 03, 12, 00, 00, DateTimeKind.Utc);

        var encoded = _Encode(_RedisValue(value), isNull: false, logical, logical.AddMinutes(5));
        var decoded = RedisCacheEntryFrame.DecodeMemory(encoded);

        decoded.IsFramed.Should().BeTrue();
        decoded.LogicalExpiresAt.Should().Be(logical);
        decoded.ValueSegment.ToArray().Should().Equal(value);
    }

    private static byte[] _Encode(
        RedisValue value,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt,
        TimeSpan? slidingExpiration = null,
        DateTime? eagerRefreshAt = null,
        string? etag = null,
        DateTime? lastModifiedAt = null,
        IReadOnlyCollection<string>? tags = null,
        DateTime? createdAt = null
    ) =>
        RedisCacheEntryFrame.Encode(
            value,
            isNull,
            logicalExpiresAt,
            physicalExpiresAt,
            slidingExpiration,
            eagerRefreshAt,
            etag,
            lastModifiedAt,
            tags,
            createdAt
        );

    private static RedisValue _RedisValue(byte[] value) => value;

    private static RedisCacheEntryFrame.DecodedFrame _Decode(RedisValue value) => RedisCacheEntryFrame.Decode(value);
}
