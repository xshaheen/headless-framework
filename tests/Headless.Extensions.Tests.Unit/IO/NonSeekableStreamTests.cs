// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Headless.Testing.Tests;

namespace Tests.IO;

public sealed class NonSeekableStreamTests : TestBase
{
    [Fact]
    public void CanSeek_should_return_false()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new NonSeekableStream(inner);

        // when/then
        sut.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void Seek_should_throw_NotSupportedException()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3]);
        using var sut = new NonSeekableStream(inner);

        // when
        var act = () => sut.Seek(0, SeekOrigin.Begin);

        // then
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Position_set_should_throw_NotSupportedException()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3]);
        using var sut = new NonSeekableStream(inner);

        // when
        var act = () => sut.Position = 0;

        // then
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Length_should_throw_NotSupportedException()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3]);
        using var sut = new NonSeekableStream(inner);

        // when
        var act = () => _ = sut.Length;

        // then
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SetLength_should_throw_NotSupportedException()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new NonSeekableStream(inner);

        // when
        var act = () => sut.SetLength(100);

        // then
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void should_delegate_CanRead_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new NonSeekableStream(inner);

        // when/then
        sut.CanRead.Should().Be(inner.CanRead);
    }

    [Fact]
    public void should_delegate_CanWrite_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new NonSeekableStream(inner);

        // when/then
        sut.CanWrite.Should().Be(inner.CanWrite);
    }

    [Fact]
    public void Position_get_should_throw_NotSupportedException()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3, 4, 5]);
        inner.Position = 3;
        using var sut = new NonSeekableStream(inner);

        // when - the getter must not leak the seekable inner position; CanSeek is hard-false.
        var act = () => _ = sut.Position;

        // then
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Position_get_should_throw_ObjectDisposedException_when_disposed()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3]);
        var sut = new NonSeekableStream(inner);
        sut.Dispose();

        // when
        var act = () => _ = sut.Position;

        // then
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void should_delegate_Read_to_inner_stream()
    {
        // given
        byte[] data = [10, 20, 30, 40, 50];
        using var inner = new MemoryStream(data);
        using var sut = new NonSeekableStream(inner);
        var buffer = new byte[3];

        // when
        var bytesRead = sut.Read(buffer, 0, 3);

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
    }

    [Fact]
    public void should_delegate_Write_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new NonSeekableStream(inner);
        byte[] data = [1, 2, 3];

        // when
        sut.Write(data, 0, 3);

        // then
        inner.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public void should_delegate_Flush_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new NonSeekableStream(inner);

        // when/then - no exception means delegation works
        sut.Flush();
    }

    [Fact]
    public void should_delegate_Close_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        var sut = new NonSeekableStream(inner);

        // when
        sut.Close();

        // then - inner stream should be closed
        var act = () => inner.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void should_dispose_inner_stream_on_Dispose()
    {
        // given
        using var inner = new MemoryStream();
        var sut = new NonSeekableStream(inner);

        // when
        sut.Dispose();

        // then
        var act = () => inner.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void should_set_IsDisposed_on_Dispose()
    {
        // given
        using var inner = new MemoryStream();
        var sut = new NonSeekableStream(inner);

        // when
        sut.Dispose();

        // then
        sut.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void should_handle_multiple_Dispose_calls()
    {
        // given
        using var inner = new MemoryStream();
        var sut = new NonSeekableStream(inner);

        // when
        sut.Dispose();
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
        var act = () => sut.Dispose();
#pragma warning restore MA0045

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void Seek_should_throw_ObjectDisposedException_when_disposed()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3]);
        var sut = new NonSeekableStream(inner);
        sut.Dispose();

        // when
        var act = () => sut.Seek(0, SeekOrigin.Begin);

        // then
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Position_set_should_throw_ObjectDisposedException_when_disposed()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3]);
        var sut = new NonSeekableStream(inner);
        sut.Dispose();

        // when
        var act = () => sut.Position = 0;

        // then
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_delegate_ReadAsync_byte_array_to_inner_stream()
    {
        // given
        byte[] data = [10, 20, 30, 40, 50];
        await using var inner = new MemoryStream(data);
        await using var sut = new NonSeekableStream(inner);
        var buffer = new byte[3];

        // when
        var bytesRead = await sut.ReadAsync(buffer.AsMemory(0, 3), AbortToken);

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
    }

    [Fact]
    public async Task should_delegate_ReadAsync_memory_to_inner_stream()
    {
        // given
        byte[] data = [10, 20, 30, 40, 50];
        await using var inner = new MemoryStream(data);
        await using var sut = new NonSeekableStream(inner);
        var buffer = new byte[3];

        // when
        var bytesRead = await sut.ReadAsync(buffer.AsMemory(), AbortToken);

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
    }

    [Fact]
    public void should_delegate_Read_span_to_inner_stream()
    {
        // given
        byte[] data = [10, 20, 30, 40, 50];
        using var inner = new MemoryStream(data);
        using var sut = new NonSeekableStream(inner);
        var buffer = new byte[3];

        // when
        var bytesRead = sut.Read(buffer.AsSpan());

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
    }

    [Fact]
    public void should_delegate_Write_span_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new NonSeekableStream(inner);
        byte[] data = [1, 2, 3];

        // when
        sut.Write(data.AsSpan());

        // then
        inner.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task should_delegate_WriteAsync_byte_array_to_inner_stream()
    {
        // given
        await using var inner = new MemoryStream();
        await using var sut = new NonSeekableStream(inner);
        byte[] data = [1, 2, 3];

        // when
        await sut.WriteAsync(data.AsMemory(0, 3), AbortToken);

        // then
        inner.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task should_delegate_WriteAsync_memory_to_inner_stream()
    {
        // given
        await using var inner = new MemoryStream();
        await using var sut = new NonSeekableStream(inner);
        byte[] data = [1, 2, 3];

        // when
        await sut.WriteAsync(data.AsMemory(), AbortToken);

        // then
        inner.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task should_delegate_FlushAsync_to_inner_stream()
    {
        // given
        await using var inner = new MemoryStream();
        await using var sut = new NonSeekableStream(inner);

        // when/then - no exception means delegation works
        await sut.FlushAsync(AbortToken);
    }

    [Fact]
    public void Close_should_dispose_inner_stream_exactly_once()
    {
        // given
        var inner = new DisposeCountingStream();
        var sut = new NonSeekableStream(inner);

        // when - Close routes through Dispose(true); the inner stream must not be disposed twice.
        sut.Close();

        // then
        inner.DisposeCount.Should().Be(1);
    }

    private sealed class DisposeCountingStream : MemoryStream
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }
}
