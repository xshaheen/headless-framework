// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using Headless.Idempotency;
using Headless.Idempotency.Caching;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The framed byte[] the cache stores an idempotency record as: header JSON, then the result and recovery state raw,
/// then their lengths. The footer order and the raw segments are the contract every cache provider relies on.
/// </summary>
public sealed class CacheIdempotencyEntryTests : TestBase
{
    private static byte[] _Bytes(int size)
    {
        var data = new byte[size];

        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(((i * 7) + 13) % 251);
        }

        return data;
    }

    private static CacheIdempotencyEntry _Entry(byte[]? result = null, byte[]? recoveryState = null)
    {
        return new CacheIdempotencyEntry(
            IdempotencyRecordStatus.Completed,
            "SHA-256",
            _Bytes(32),
            Generation: 7,
            LeaseExpiresAt: null,
            Result: result,
            ResultContract: result is null ? null : "headless.idempotency.response/v2",
            RetentionUntil: new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero),
            RecoveryPoint: recoveryState is null ? null : "after-charge",
            RecoveryState: recoveryState,
            RecoveryContract: recoveryState is null ? null : "test.recovery/v1",
            HighestGeneration: 9
        );
    }

    [Fact]
    public void should_round_trip_an_entry_with_result_and_recovery_state()
    {
        var entry = _Entry(result: _Bytes(256 * 1024), recoveryState: _Bytes(512));

        var decoded = CacheIdempotencyEntry.Deserialize("key", entry.Encode());

        decoded.Should().BeEquivalentTo(entry);
    }

    [Fact]
    public void should_round_trip_an_entry_with_no_result_and_no_recovery_state()
    {
        var entry = _Entry();

        var decoded = CacheIdempotencyEntry.Deserialize("key", entry.Encode());

        decoded.Should().BeEquivalentTo(entry);
        decoded.Result.Should().BeNull();
        decoded.RecoveryState.Should().BeNull();
    }

    [Fact]
    public void should_round_trip_an_entry_with_only_a_result()
    {
        // The two footer lengths are independent: a result with no recovery state must read back in order, not
        // with the footer's two lengths exchanged.
        var entry = _Entry(result: _Bytes(1024));

        var decoded = CacheIdempotencyEntry.Deserialize("key", entry.Encode());

        decoded.Result.Should().NotBeNull().And.Equal(entry.Result!);
        decoded.RecoveryState.Should().BeNull();
    }

    [Fact]
    public void should_round_trip_an_entry_with_only_a_recovery_state()
    {
        var entry = _Entry(recoveryState: _Bytes(1024));

        var decoded = CacheIdempotencyEntry.Deserialize("key", entry.Encode());

        decoded.Result.Should().BeNull();
        decoded.RecoveryState.Should().NotBeNull().And.Equal(entry.RecoveryState!);
    }

    [Fact]
    public void should_refuse_bytes_shorter_than_the_footer()
    {
        var act = () => CacheIdempotencyEntry.Deserialize("key", new byte[7]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an idempotency record*");
    }

    [Fact]
    public void should_refuse_bytes_whose_footer_lengths_exceed_the_payload()
    {
        var payload = _Entry().Encode();

        // Both lengths impossibly large: no framing this version wrote can satisfy them.
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(payload.Length - (2 * sizeof(int))), 1 << 20);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(payload.Length - sizeof(int)), 1 << 20);
        var act = () => CacheIdempotencyEntry.Deserialize("key", payload);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an idempotency record*");
    }

    [Fact]
    public void should_refuse_bytes_whose_footer_lengths_are_below_the_null_marker()
    {
        var payload = _Entry().Encode();

        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(payload.Length - (2 * sizeof(int))), -2);
        var act = () => CacheIdempotencyEntry.Deserialize("key", payload);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an idempotency record*");
    }

    [Fact]
    public void should_refuse_plain_json_that_is_not_a_frame()
    {
        // The shape an older version stored — JSON text holding a base64 result — must be refused, not misread.
        var act = () => CacheIdempotencyEntry.Deserialize("key", "{\"Status\":2}"u8.ToArray());

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an idempotency record*");
    }
}
