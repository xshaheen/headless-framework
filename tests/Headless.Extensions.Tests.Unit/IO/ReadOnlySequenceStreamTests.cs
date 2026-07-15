using System.Buffers;
using Headless.IO;
using Headless.Testing.Tests;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests.IO;

public sealed class ReadOnlySequenceStreamTests : TestBase
{
    private static readonly ReadOnlySequence<byte> _DefaultSequence = ReadOnlySequence<byte>.Empty;
    private static readonly ReadOnlySequence<byte> _SimpleSequence = new([1, 2, 3]);
    private static readonly ReadOnlySequence<byte> _MultiBlockSequence = _CreateMultiBlockSequence();
    private readonly Stream _defaultStream = _DefaultSequence.ToStream();

    protected override async ValueTask DisposeAsyncCore()
    {
        await _defaultStream.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public void read_empty_sequence()
    {
        _defaultStream.Read(new byte[1], 0, 1).Should().Be(0);
    }

    [Fact]
    public void length()
    {
        _defaultStream.Length.Should().Be(0);
        _defaultStream.Dispose();
        var act = () => _ = _defaultStream.Length;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void set_length()
    {
        var act = () => _defaultStream.SetLength(0);
        act.Should().Throw<NotSupportedException>();
        _defaultStream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void can_seek()
    {
        _defaultStream.CanSeek.Should().BeTrue();
        _defaultStream.Dispose();
        _defaultStream.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void can_read()
    {
        _defaultStream.CanRead.Should().BeTrue();
        _defaultStream.Dispose();
        _defaultStream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void can_write()
    {
        _defaultStream.CanWrite.Should().BeFalse();
        _defaultStream.Dispose();
        _defaultStream.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void can_timeout()
    {
        _defaultStream.CanTimeout.Should().BeFalse();
        _defaultStream.Dispose();
        _defaultStream.CanTimeout.Should().BeFalse();
    }

    [Fact]
    public void position()
    {
        _defaultStream.Position.Should().Be(0);
        var act = () => _defaultStream.Position = 1;
        act.Should().Throw<ArgumentOutOfRangeException>();

        var simpleStream = _SimpleSequence.ToStream();
        simpleStream.Position.Should().Be(0);
        simpleStream.Position++;
        simpleStream.Position.Should().Be(1);

        var multiBlockStream = _MultiBlockSequence.ToStream();
        (multiBlockStream.Position = 0).Should().Be(0);

        // ReadByte advances past the pre-read position, so the expected value must be captured first.
        var expected = (int)multiBlockStream.Position + 1;
        multiBlockStream.ReadByte().Should().Be(expected);

        (multiBlockStream.Position = 4).Should().Be(4);
        expected = (int)multiBlockStream.Position + 1;
        multiBlockStream.ReadByte().Should().Be(expected);

        (multiBlockStream.Position = 5).Should().Be(5);
        expected = (int)multiBlockStream.Position + 1;
        multiBlockStream.ReadByte().Should().Be(expected);

        (multiBlockStream.Position = 0).Should().Be(0);
        expected = (int)multiBlockStream.Position + 1;
        multiBlockStream.ReadByte().Should().Be(expected);

        (multiBlockStream.Position = 9).Should().Be(9);
        multiBlockStream.ReadByte().Should().Be(-1);

        var actTooHigh = () => multiBlockStream.Position = 10;
        actTooHigh.Should().Throw<ArgumentOutOfRangeException>();
        var actNegative = () => multiBlockStream.Position = -1;
        actNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void flush()
    {
        var act = () => _defaultStream.Flush();
        act.Should().Throw<NotSupportedException>();
        _defaultStream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task flush_async()
    {
        var act = () => _defaultStream.FlushAsync(AbortToken);
        await act.Should().ThrowAsync<NotSupportedException>();
        await _defaultStream.DisposeAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void write()
    {
        var act = () => _defaultStream.Write(new byte[1], 0, 1);
        act.Should().Throw<NotSupportedException>();
        _defaultStream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task write_async()
    {
        var act = () => _defaultStream.WriteAsync(new byte[1], 0, 1, AbortToken);
        await act.Should().ThrowAsync<NotSupportedException>();
        await _defaultStream.DisposeAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void write_byte()
    {
        var act = () => _defaultStream.WriteByte(1);
        act.Should().Throw<NotSupportedException>();
        _defaultStream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void seek_empty_stream()
    {
        var stream = _DefaultSequence.ToStream();
        stream.Seek(0, SeekOrigin.Begin).Should().Be(0);
        stream.Seek(0, SeekOrigin.Current).Should().Be(0);
        stream.Seek(0, SeekOrigin.End).Should().Be(0);
    }

    [Fact]
    public void seek()
    {
        var stream = _MultiBlockSequence.ToStream();
        stream.Seek(0, SeekOrigin.Begin).Should().Be(0);
        stream.Position.Should().Be(0);
        // ReadByte advances past the pre-read position, so the expected value must be captured first.
        var expected = (int)stream.Position + 1;
        stream.ReadByte().Should().Be(expected);

        stream.Seek(4, SeekOrigin.Begin).Should().Be(4);
        stream.Position.Should().Be(4);
        expected = (int)stream.Position + 1;
        stream.ReadByte().Should().Be(expected);

        stream.Seek(7, SeekOrigin.Begin).Should().Be(7);
        stream.Position.Should().Be(7);
        expected = (int)stream.Position + 1;
        stream.ReadByte().Should().Be(expected);

        stream.Seek(1, SeekOrigin.Current).Should().Be(9);
        stream.Position.Should().Be(9);

        stream.Seek(-8, SeekOrigin.Current).Should().Be(1);
        stream.Position.Should().Be(1);
        expected = (int)stream.Position + 1;
        stream.ReadByte().Should().Be(expected);

        stream.Seek(3, SeekOrigin.Current).Should().Be(5);
        stream.Position.Should().Be(5);
        expected = (int)stream.Position + 1;
        stream.ReadByte().Should().Be(expected);

        stream.Position = 0;
        stream.Seek(0, SeekOrigin.End).Should().Be(9);
        stream.Position.Should().Be(9);
        stream.ReadByte().Should().Be(-1);

        stream.Position = 0;
        stream.Seek(-1, SeekOrigin.End).Should().Be(8);
        stream.Position.Should().Be(8);
        expected = (int)stream.Position + 1;
        stream.ReadByte().Should().Be(expected);

        stream.Seek(-4, SeekOrigin.End).Should().Be(5);
        stream.Position.Should().Be(5);
        expected = (int)stream.Position + 1;
        stream.ReadByte().Should().Be(expected);

        var actPastEnd = () => stream.Seek(1, SeekOrigin.End);
        actPastEnd.Should().Throw<ArgumentOutOfRangeException>();
        // Seeking before the beginning now surfaces as an IOException (was ArgumentOutOfRangeException).
        var actBeforeBegin = () => stream.Seek(-1, SeekOrigin.Begin);
        actBeforeBegin.Should().Throw<IOException>();

        stream.Dispose();
        var actDisposed = () => stream.Seek(0, SeekOrigin.Begin);
        actDisposed.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void seek_before_begin_throws_io_exception()
    {
        var stream = _MultiBlockSequence.ToStream();

        // Begin with a negative offset.
        var actBegin = () => stream.Seek(-1, SeekOrigin.Begin);
        actBegin.Should().Throw<IOException>();

        // Current that underflows past the start.
        stream.Position = 2;
        var actCurrent = () => stream.Seek(-5, SeekOrigin.Current);
        actCurrent.Should().Throw<IOException>();

        // End that underflows past the start.
        var actEnd = () => stream.Seek(-(_MultiBlockSequence.Length + 1), SeekOrigin.End);
        actEnd.Should().Throw<IOException>();
    }

    [Fact]
    public void read_byte()
    {
        var stream = _MultiBlockSequence.ToStream();

        for (var i = 0; i < _MultiBlockSequence.Length; i++)
        {
            stream.ReadByte().Should().Be(i + 1);
        }

        stream.ReadByte().Should().Be(-1);
        stream.ReadByte().Should().Be(-1);
    }

    [Fact]
    public void read()
    {
        var stream = _MultiBlockSequence.ToStream();
        var buffer = new byte[_MultiBlockSequence.Length + 2];
        stream.Read(buffer, 0, 2).Should().Be(2);
        buffer.Take(3).Should().Equal([1, 2, 0]);
        stream.Position.Should().Be(2);

        stream.Read(buffer, 3, 2).Should().Be(2);
        buffer.Take(6).Should().Equal([1, 2, 0, 3, 4, 0]);

        stream.Read(buffer, 5, buffer.Length - 5).Should().Be(5);
        buffer.Should().Equal([1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 0]);
        stream.Position.Should().Be(9);

        stream.Read(buffer, 0, buffer.Length).Should().Be(0);
        stream.Read(buffer, 0, buffer.Length).Should().Be(0);
        stream.Position.Should().Be(9);
    }

    [Fact]
    public void read_async_returns_synchronously()
    {
        var stream = _SimpleSequence.ToStream();
        stream.ReadAsync(new byte[1], 0, 1, AbortToken).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task read_async_reuses_task_result()
    {
        var stream = _MultiBlockSequence.ToStream();
        var task1 = stream.ReadAsync(new byte[1], 0, 1, AbortToken);
        var task2 = stream.ReadAsync(new byte[1], 0, 1, AbortToken);
        task2.Should().BeSameAs(task1);
        (await task1).Should().Be(1);

        var task3 = stream.ReadAsync(new byte[2], 0, 2, AbortToken);
        var task4 = stream.ReadAsync(new byte[2], 0, 2, AbortToken);
        task4.Should().BeSameAs(task3);
        (await task3).Should().Be(2);
    }

    [Fact]
    public async Task read_async_works()
    {
        var stream = _MultiBlockSequence.ToStream();
        var buffer = new byte[_MultiBlockSequence.Length + 2];
        (await stream.ReadAsync(buffer.AsMemory(0, 2), AbortToken)).Should().Be(2);
        buffer.Take(3).Should().Equal([1, 2, 0]);
        stream.Position.Should().Be(2);

        (await stream.ReadAsync(buffer.AsMemory(3, 2), AbortToken)).Should().Be(2);
        buffer.Take(6).Should().Equal([1, 2, 0, 3, 4, 0]);

        (await stream.ReadAsync(buffer.AsMemory(5, buffer.Length - 5), AbortToken)).Should().Be(5);
        buffer.Should().Equal([1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 0]);
        stream.Position.Should().Be(9);

        (await stream.ReadAsync(buffer, AbortToken)).Should().Be(0);
        stream.Position.Should().Be(9);
    }

    [Fact]
    public async Task copy_to_async()
    {
        var stream = _MultiBlockSequence.ToStream();
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, AbortToken);
        ms.ToArray().Should().Equal(_MultiBlockSequence.ToArray());
    }

    [Fact]
    public async Task copy_to_async_after_partial_read_copies_only_remainder()
    {
        var stream = _MultiBlockSequence.ToStream();
        var head = new byte[3];
        (await stream.ReadAsync(head.AsMemory(0, 3), AbortToken)).Should().Be(3);
        stream.Position.Should().Be(3);

        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, AbortToken);

        // Only the unread remainder is copied, and the stream is drained to the end.
        ms.ToArray().Should().Equal([4, 5, 6, 7, 8, 9]);
        stream.Position.Should().Be(_MultiBlockSequence.Length);
    }

    [Fact]
    public void is_disposed()
    {
        var stream = (ReadOnlySequenceStream)_defaultStream;
        stream.IsDisposed.Should().BeFalse();
        _defaultStream.Dispose();
        stream.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void read_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        stream.Dispose();
        var act = () => stream.Read(new byte[1], 0, 1);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void read_span_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        stream.Dispose();
        var act = () => stream.Read(new byte[1].AsSpan());
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void read_byte_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        stream.Dispose();
        var act = () => stream.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task read_async_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        await stream.DisposeAsync();
        var act = () => stream.ReadAsync(new byte[1], 0, 1, AbortToken);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task read_async_memory_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        await stream.DisposeAsync();
        var act = async () => await stream.ReadAsync(new byte[1].AsMemory(), AbortToken);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void position_get_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        stream.Dispose();
        var act = () => _ = stream.Position;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void position_set_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        stream.Dispose();
        var act = () => stream.Position = 0;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task copy_to_async_after_dispose_throws()
    {
        var stream = _MultiBlockSequence.ToStream();
        await stream.DisposeAsync();
        var ms = new MemoryStream();
        var act = () => stream.CopyToAsync(ms, AbortToken);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private sealed class SeqSegment : ReadOnlySequenceSegment<byte>
    {
        public SeqSegment(byte[] buffer, SeqSegment? next)
        {
            Memory = buffer;
            Next = next;

            var current = this;

            while (next != null)
            {
                next.RunningIndex = current.RunningIndex + current.Memory.Length;
                current = next;
                next = (SeqSegment?)next.Next;
            }
        }
    }

    private static ReadOnlySequence<byte> _CreateMultiBlockSequence()
    {
        var seg3 = new SeqSegment([7, 8, 9], null);
        var seg2 = new SeqSegment([4, 5, 6], seg3);
        var seg1 = new SeqSegment([1, 2, 3], seg2);

        return new ReadOnlySequence<byte>(seg1, 0, seg3, seg3.Memory.Length);
    }
}
