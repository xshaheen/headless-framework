// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Serializer;

namespace Tests;

public sealed class PooledByteBufferWriterTests
{
    [Fact]
    public void should_preserve_written_bytes_when_buffer_grows()
    {
        // given
        using var writer = new PooledByteBufferWriter();
        var first = Enumerable.Range(0, 256).Select(static value => (byte)value).ToArray();
        var second = Enumerable.Range(0, 300).Select(static value => (byte)(value % 251)).ToArray();

        writer.Write(first);

        // when
        writer.Write(second);

        // then
        writer.WrittenSpan.ToArray().Should().Equal([.. first, .. second]);
        writer.WrittenMemory.ToArray().Should().Equal([.. first, .. second]);
    }

    [Fact]
    public void should_reject_negative_advance_without_changing_written_length()
    {
        // given
        using var writer = new PooledByteBufferWriter();

        // when
        var act = () => writer.Advance(-1);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("Cannot advance past the end of the buffer.");
        writer.WrittenSpan.ToArray().Should().BeEmpty();
    }

    [Fact]
    public void should_reject_advance_beyond_granted_buffer_without_changing_written_length()
    {
        // given
        using var writer = new PooledByteBufferWriter();
        var grantedLength = writer.GetSpan().Length;

        // when
        var act = () => writer.Advance(grantedLength + 1);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("Cannot advance past the end of the buffer.");
        writer.WrittenSpan.ToArray().Should().BeEmpty();
    }
}
