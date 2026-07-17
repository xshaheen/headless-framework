// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Core;

public sealed class ByteExtensionsTests
{
    [Fact]
    public void should_round_trip_incompressible_data_when_compress_then_decompress()
    {
        // given - a non-trivial random payload large enough that BrotliStream buffers its trailing bytes;
        // the previous Compress() read the MemoryStream while the BrotliStream was still open and truncated it.
        var original = new byte[4096];
        new Random(12345).NextBytes(original);

        // when
        var roundTripped = original.Compress().Decompress();

        // then
        roundTripped.Should().Equal(original);
    }

    [Fact]
    public void should_round_trip_repetitive_data_when_compress_then_decompress()
    {
        // given - highly compressible data whose compressed frame is only flushed on stream dispose
        var original = Enumerable.Repeat((byte)'A', 10_000).ToArray();

        // when
        var roundTripped = original.Compress().Decompress();

        // then
        roundTripped.Should().Equal(original);
    }

    [Fact]
    public void should_round_trip_empty_array_when_compress_then_decompress()
    {
        // given
        var original = Array.Empty<byte>();

        // when
        var roundTripped = original.Compress().Decompress();

        // then
        roundTripped.Should().BeEmpty();
    }
}
