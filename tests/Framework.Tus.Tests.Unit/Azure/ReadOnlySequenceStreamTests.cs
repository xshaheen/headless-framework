// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Framework.Testing.Tests;
using Framework.Tus.Internal;

namespace Tests.Azure;

public sealed class ReadOnlySequenceStreamTests : TestBase
{
    #region Constructor and Properties

    [Fact]
    public void should_report_can_read_true()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // then
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public void should_report_can_seek_true()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // then
        stream.CanSeek.Should().BeTrue();
    }

    [Fact]
    public void should_report_can_write_false()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // then
        stream.CanWrite.Should().BeFalse();
    }

    #endregion

    #region Length Property

    [Fact]
    public void should_report_correct_length()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // then
        stream.Length.Should().Be(5);
    }

    [Fact]
    public void should_report_zero_length_for_empty_sequence()
    {
        // given
        var sequence = new ReadOnlySequence<byte>([]);
        using var stream = new ReadOnlySequenceStream(sequence);

        // then
        stream.Length.Should().Be(0);
    }

    #endregion

    #region Position Property

    [Fact]
    public void should_report_initial_position_zero()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // then
        stream.Position.Should().Be(0);
    }

    [Fact]
    public void should_update_position_after_read()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer = new byte[3];

        // when
        _ = stream.Read(buffer, 0, 3);

        // then
        stream.Position.Should().Be(3);
    }

    [Fact]
    public void should_set_position()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        stream.Position = 3;

        // then
        stream.Position.Should().Be(3);
    }

    [Fact]
    public void should_throw_when_position_is_negative()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.Position = -1;

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_when_position_exceeds_length()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.Position = 4;

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_allow_position_at_end_of_stream()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        stream.Position = 3;

        // then
        stream.Position.Should().Be(3);
    }

    #endregion

    #region Read Operations

    [Fact]
    public void should_read_from_sequence()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer = new byte[5];

        // when
        var bytesRead = stream.Read(buffer, 0, 5);

        // then
        bytesRead.Should().Be(5);
        buffer.Should().BeEquivalentTo([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void should_read_partial_data()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer = new byte[3];

        // when
        var bytesRead = stream.Read(buffer, 0, 3);

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void should_read_with_offset_in_buffer()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer = new byte[5];
        buffer[0] = 99;
        buffer[1] = 99;

        // when
        var bytesRead = stream.Read(buffer, 2, 3);

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo([99, 99, 1, 2, 3]);
    }

    [Fact]
    public void should_read_less_when_count_exceeds_remaining()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer = new byte[10];

        // when
        var bytesRead = stream.Read(buffer, 0, 10);

        // then
        bytesRead.Should().Be(3);
    }

    [Fact]
    public void should_return_zero_when_at_end()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        stream.Position = 3;
        var buffer = new byte[10];

        // when
        var bytesRead = stream.Read(buffer, 0, 10);

        // then
        bytesRead.Should().Be(0);
    }

    [Fact]
    public void should_read_from_empty_sequence()
    {
        // given
        var sequence = new ReadOnlySequence<byte>([]);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer = new byte[10];

        // when
        var bytesRead = stream.Read(buffer, 0, 10);

        // then
        bytesRead.Should().Be(0);
    }

    [Fact]
    public void should_read_sequentially()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer1 = new byte[2];
        var buffer2 = new byte[3];

        // when
        var bytesRead1 = stream.Read(buffer1, 0, 2);
        var bytesRead2 = stream.Read(buffer2, 0, 3);

        // then
        bytesRead1.Should().Be(2);
        bytesRead2.Should().Be(3);
        buffer1.Should().BeEquivalentTo([1, 2]);
        buffer2.Should().BeEquivalentTo([3, 4, 5]);
    }

    #endregion

    #region Read Span Overload

    [Fact]
    public void should_read_into_span()
    {
        // given
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        Span<byte> buffer = stackalloc byte[5];

        // when
        var bytesRead = stream.Read(buffer);

        // then
        bytesRead.Should().Be(5);
        buffer.ToArray().Should().BeEquivalentTo([10, 20, 30, 40, 50]);
    }

    [Fact]
    public void should_read_partial_into_span()
    {
        // given
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        Span<byte> buffer = stackalloc byte[3];

        // when
        var bytesRead = stream.Read(buffer);

        // then
        bytesRead.Should().Be(3);
        buffer.ToArray().Should().BeEquivalentTo([10, 20, 30]);
        stream.Position.Should().Be(3);
    }

    [Fact]
    public void should_return_zero_when_reading_span_at_end()
    {
        // given
        var data = new byte[] { 1, 2 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        stream.Position = 2;
        Span<byte> buffer = stackalloc byte[5];

        // when
        var bytesRead = stream.Read(buffer);

        // then
        bytesRead.Should().Be(0);
    }

    #endregion

    #region Seek Operations

    [Fact]
    public void should_seek_from_begin()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var result = stream.Seek(3, SeekOrigin.Begin);

        // then
        result.Should().Be(3);
        stream.Position.Should().Be(3);
    }

    [Fact]
    public void should_seek_from_current()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        stream.Position = 2;

        // when
        var result = stream.Seek(2, SeekOrigin.Current);

        // then
        result.Should().Be(4);
        stream.Position.Should().Be(4);
    }

    [Fact]
    public void should_seek_backward_from_current()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);
        stream.Position = 4;

        // when
        var result = stream.Seek(-2, SeekOrigin.Current);

        // then
        result.Should().Be(2);
        stream.Position.Should().Be(2);
    }

    [Fact]
    public void should_seek_from_end()
    {
        // given
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var result = stream.Seek(-2, SeekOrigin.End);

        // then
        result.Should().Be(3);
        stream.Position.Should().Be(3);
    }

    [Fact]
    public void should_seek_to_end()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var result = stream.Seek(0, SeekOrigin.End);

        // then
        result.Should().Be(3);
        stream.Position.Should().Be(3);
    }

    [Fact]
    public void should_throw_when_seek_position_negative()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.Seek(-1, SeekOrigin.Begin);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_when_seek_position_exceeds_length()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.Seek(10, SeekOrigin.Begin);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_for_invalid_seek_origin()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.Seek(0, (SeekOrigin)99);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Write Operations - Not Supported

    [Fact]
    public void should_throw_on_write()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.Write([4, 5, 6], 0, 3);

        // then
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void should_throw_on_set_length()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.SetLength(10);

        // then
        act.Should().Throw<NotSupportedException>();
    }

    #endregion

    #region Flush

    [Fact]
    public void should_not_throw_on_flush()
    {
        // given
        var data = new byte[] { 1, 2, 3 };
        var sequence = new ReadOnlySequence<byte>(data);
        using var stream = new ReadOnlySequenceStream(sequence);

        // when
        var act = () => stream.Flush();

        // then
        act.Should().NotThrow();
    }

    #endregion

    #region Multi-Segment Sequence

    [Fact]
    public void should_read_from_multi_segment_sequence()
    {
        // given - create a multi-segment sequence
        var segment1 = new byte[] { 1, 2, 3 };
        var segment2 = new byte[] { 4, 5, 6 };
        var segment3 = new byte[] { 7, 8, 9 };

        var first = new TestMemorySegment<byte>(segment1);
        var second = first.Append(segment2);
        var third = second.Append(segment3);

        var sequence = new ReadOnlySequence<byte>(first, 0, third, third.Memory.Length);
        using var stream = new ReadOnlySequenceStream(sequence);
        var buffer = new byte[9];

        // when
        var bytesRead = stream.Read(buffer, 0, 9);

        // then
        bytesRead.Should().Be(9);
        buffer.Should().BeEquivalentTo([1, 2, 3, 4, 5, 6, 7, 8, 9]);
    }

    [Fact]
    public void should_read_across_segment_boundaries()
    {
        // given - create a multi-segment sequence
        var segment1 = new byte[] { 1, 2 };
        var segment2 = new byte[] { 3, 4 };

        var first = new TestMemorySegment<byte>(segment1);
        var second = first.Append(segment2);

        var sequence = new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
        using var stream = new ReadOnlySequenceStream(sequence);

        // read first byte
        var buffer1 = new byte[1];
        _ = stream.Read(buffer1, 0, 1);

        // read across boundary
        var buffer2 = new byte[2];
        var bytesRead = stream.Read(buffer2, 0, 2);

        // then
        bytesRead.Should().Be(2);
        buffer2.Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public void should_report_correct_length_for_multi_segment()
    {
        // given
        var segment1 = new byte[] { 1, 2, 3 };
        var segment2 = new byte[] { 4, 5 };

        var first = new TestMemorySegment<byte>(segment1);
        var second = first.Append(segment2);

        var sequence = new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
        using var stream = new ReadOnlySequenceStream(sequence);

        // then
        stream.Length.Should().Be(5);
    }

    #endregion

    #region Helper Class

    private sealed class TestMemorySegment<T> : ReadOnlySequenceSegment<T>
    {
        public TestMemorySegment(ReadOnlyMemory<T> memory)
        {
            Memory = memory;
        }

        public TestMemorySegment<T> Append(ReadOnlyMemory<T> memory)
        {
            var segment = new TestMemorySegment<T>(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }

    #endregion
}
