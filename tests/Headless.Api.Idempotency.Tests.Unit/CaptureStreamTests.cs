// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests;

public sealed class CaptureStreamTests : TestBase
{
    private const int _Cap = 100;

    [Fact]
    public void write_forwards_to_inner_and_populates_captured_bytes()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);
        byte[] data = [1, 2, 3];

        capture.Write(data, 0, data.Length);

        inner.ToArray().Should().Equal(data);
        capture.CapturedBytes.Should().Equal(data);
        capture.TruncatedCapture.Should().BeFalse();
    }

    [Fact]
    public async Task write_async_forwards_correctly()
    {
        var inner = new MemoryStream();
        await using var capture = new CaptureStream(inner, _Cap);
        byte[] data = [10, 20, 30];

        await capture.WriteAsync(data.AsMemory(), AbortToken);

        inner.ToArray().Should().Equal(data);
        capture.CapturedBytes.Should().Equal(data);
    }

    [Fact]
    public async Task write_async_memory_forwards_correctly()
    {
        var inner = new MemoryStream();
        await using var capture = new CaptureStream(inner, _Cap);
        byte[] data = [5, 6, 7];

        await capture.WriteAsync(data.AsMemory(), AbortToken);

        inner.ToArray().Should().Equal(data);
        capture.CapturedBytes.Should().Equal(data);
    }

    [Fact]
    public void write_span_forwards_correctly()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);
        byte[] data = [9, 8, 7];

        capture.Write(data.AsSpan());

        inner.ToArray().Should().Equal(data);
        capture.CapturedBytes.Should().Equal(data);
    }

    [Fact]
    public void write_byte_forwards_correctly()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        capture.WriteByte(0x42);

        inner.ToArray().Should().Equal("B"u8.ToArray());
        capture.CapturedBytes.Should().Equal("B"u8.ToArray());
    }

    [Fact]
    public void empty_write_leaves_captured_bytes_unchanged()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        capture.Write([], 0, 0);

        inner.Length.Should().Be(0);
        capture.CapturedBytes.Should().BeEmpty();
        capture.TruncatedCapture.Should().BeFalse();
    }

    [Fact]
    public void write_exceeding_cap_truncates_buffer_but_inner_receives_full_payload()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, cap: 5);
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];

        capture.Write(data, 0, data.Length);

        inner.ToArray().Should().Equal(data);
        capture.CapturedBytes.Should().HaveCount(5);
        capture.TruncatedCapture.Should().BeTrue();
    }

    [Fact]
    public void multiple_writes_concatenate_in_order_up_to_cap()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        capture.Write([1, 2, 3], 0, 3);
        capture.Write([4, 5, 6], 0, 3);
        capture.Write([7, 8, 9], 0, 3);

        capture.CapturedBytes.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void flush_forwards_to_inner_and_does_not_affect_captured_bytes()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);
        capture.Write([1, 2], 0, 2);

        capture.Flush();

        capture.CapturedBytes.Should().Equal(1, 2);
    }

    [Fact]
    public async Task flush_async_forwards_to_inner()
    {
        var inner = new MemoryStream();
        await using var capture = new CaptureStream(inner, _Cap);
        await capture.WriteAsync(new byte[] { 3, 4 }.AsMemory(), AbortToken);

        await capture.FlushAsync(AbortToken);

        capture.CapturedBytes.Should().Equal(3, 4);
    }

    [Fact]
    public void dispose_does_not_dispose_inner_stream()
    {
        var inner = new MemoryStream();
        var capture = new CaptureStream(inner, _Cap);

        capture.Dispose();

        // inner should still be usable
        inner.Write([1], 0, 1);
        inner.Length.Should().Be(1);
    }

    [Fact]
    public void writing_to_disposed_stream_throws_object_disposed_exception()
    {
        var inner = new MemoryStream();
        var capture = new CaptureStream(inner, _Cap);
        capture.Dispose();

        var act = () => capture.Write([1], 0, 1);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task writing_async_to_disposed_stream_throws_object_disposed_exception()
    {
        var inner = new MemoryStream();
        var capture = new CaptureStream(inner, _Cap);
        await capture.DisposeAsync();

        var act = async () => await capture.WriteAsync(new byte[] { 1 }.AsMemory(), AbortToken);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void truncated_writes_after_first_truncation_still_forward_to_inner()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, cap: 3);

        capture.Write([1, 2, 3, 4], 0, 4); // fills cap, triggers truncation
        capture.Write([5, 6], 0, 2); // beyond cap, inner still gets it

        inner.ToArray().Should().Equal(1, 2, 3, 4, 5, 6);
        capture.CapturedBytes.Should().HaveCount(3);
        capture.TruncatedCapture.Should().BeTrue();
    }

    [Fact]
    public void can_write_returns_false_after_dispose()
    {
        var inner = new MemoryStream();
        var capture = new CaptureStream(inner, _Cap);

        capture.Dispose();

        capture.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void can_read_is_false()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        capture.CanRead.Should().BeFalse();
    }

    [Fact]
    public void can_seek_is_false()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        capture.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void length_throws_not_supported_exception()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        var act = () => _ = capture.Length;

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void position_get_throws_not_supported_exception()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        var act = () => _ = capture.Position;

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void position_set_throws_not_supported_exception()
    {
        var inner = new MemoryStream();
        using var capture = new CaptureStream(inner, _Cap);

        var act = () => capture.Position = 0;

        act.Should().Throw<NotSupportedException>();
    }
}
