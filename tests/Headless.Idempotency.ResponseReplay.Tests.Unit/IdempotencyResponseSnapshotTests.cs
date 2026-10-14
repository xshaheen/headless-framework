// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Text.Json;
using Headless.Idempotency;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The length-framed stored response: body bytes raw, header JSON, and a 4-byte footer. The frame is what every
/// store holds, so these tests pin its round trip and its refusal of bytes that are not a frame this version wrote.
/// </summary>
public sealed class IdempotencyResponseSnapshotTests : TestBase
{
    private static IdempotencyResponseSnapshot _Snapshot(int bodySize)
    {
        var body = new byte[bodySize];

        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i % 241);
        }

        return new IdempotencyResponseSnapshot
        {
            StatusCode = 207,
            Headers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
                ["Retry-After"] = ["3", "9"],
            },
            Body = body,
        };
    }

    [Fact]
    public void should_round_trip_a_frame_through_encode_and_decode()
    {
        var snapshot = _Snapshot(1024);

        var decoded = IdempotencyResponseSnapshot.Decode(snapshot.Encode());

        decoded.StatusCode.Should().Be(snapshot.StatusCode);
        decoded.Headers.Should().BeEquivalentTo(snapshot.Headers);
        decoded.Body.Should().Equal(snapshot.Body);
    }

    [Fact]
    public void should_round_trip_a_write_to_frame_with_a_large_body()
    {
        // WriteTo is the middleware's path; a 256 KiB body proves the frame holds a large body without a copy
        // inside the header serialization.
        var snapshot = _Snapshot(256 * 1024);

        using var writer = new Headless.Serializer.PooledByteBufferWriter(snapshot.Body.Length + 256);
        IdempotencyResponseSnapshot.WriteTo(writer, snapshot.StatusCode, snapshot.Headers, snapshot.Body);
        var frame = writer.WrittenSpan.ToArray();

        var decoded = IdempotencyResponseSnapshot.Decode(frame);

        decoded.StatusCode.Should().Be(snapshot.StatusCode);
        decoded.Headers.Should().BeEquivalentTo(snapshot.Headers);
        decoded.Body.Should().Equal(snapshot.Body);
    }

    [Fact]
    public void should_store_the_body_raw_between_the_body_and_the_footer()
    {
        // The body must sit at the frame's start, contiguous and unprefixed, so a capture is one span copy.
        var snapshot = _Snapshot(64);

        var frame = snapshot.Encode();

        frame.AsSpan(0, snapshot.Body.Length).ToArray().Should().Equal(snapshot.Body);
    }

    [Fact]
    public void should_reject_a_payload_shorter_than_its_footer()
    {
        var act = () => IdempotencyResponseSnapshot.Decode(new byte[3]);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_reject_a_frame_whose_footer_length_disagrees_with_its_size()
    {
        var frame = _Snapshot(64).Encode();

        // A footer naming a header longer than the payload means the bytes are not a frame this version wrote.
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(frame.Length - sizeof(int)), frame.Length);
        var act = () => IdempotencyResponseSnapshot.Decode(frame);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_reject_a_frame_whose_body_length_is_negative()
    {
        var frame = _Snapshot(64).Encode();

        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(frame.Length - sizeof(int)), -5);
        var act = () => IdempotencyResponseSnapshot.Decode(frame);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_reject_bytes_that_are_not_a_frame()
    {
        // Plain JSON with a base64 body — the shape an older version stored — must be refused, not misread.
        var act = () => IdempotencyResponseSnapshot.Decode("{\"StatusCode\":200}"u8.ToArray());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_round_trip_an_empty_body()
    {
        var snapshot = _Snapshot(0);

        var decoded = IdempotencyResponseSnapshot.Decode(snapshot.Encode());

        decoded.Body.Should().BeEmpty();
        decoded.StatusCode.Should().Be(snapshot.StatusCode);
    }
}
