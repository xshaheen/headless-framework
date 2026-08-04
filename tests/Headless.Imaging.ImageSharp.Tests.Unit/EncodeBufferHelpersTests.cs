// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging.ImageSharp.Internals;

namespace Tests;

public sealed class EncodeBufferHelpersTests
{
    [Fact]
    public void should_pre_size_buffer_from_seekable_source_length()
    {
        // given
        using var source = new MemoryStream(new byte[1024]);

        // when
        using var buffer = EncodeBufferHelpers.CreateEncodeBuffer(source);

        // then
        buffer.Capacity.Should().Be(1024);
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public void should_not_pre_size_buffer_for_non_seekable_source()
    {
        // given
        using var source = new NonSeekableStream(length: 1024);

        // when
        using var buffer = EncodeBufferHelpers.CreateEncodeBuffer(source);

        // then
        buffer.Capacity.Should().Be(0);
    }

    [Fact]
    public void should_not_pre_size_buffer_for_empty_source()
    {
        // given
        using var source = new MemoryStream();

        // when
        using var buffer = EncodeBufferHelpers.CreateEncodeBuffer(source);

        // then
        buffer.Capacity.Should().Be(0);
    }

    [Fact]
    public void should_not_pre_size_buffer_beyond_the_capacity_cap()
    {
        // given: a seekable source reporting a length just past the 32 MiB cap, without allocating it
        using var source = new FixedLengthSeekableStream(length: (32L * 1024 * 1024) + 1);

        // when
        using var buffer = EncodeBufferHelpers.CreateEncodeBuffer(source);

        // then
        buffer.Capacity.Should().Be(0);
    }

    [Fact]
    public void should_pre_size_buffer_at_exactly_the_capacity_cap()
    {
        // given
        using var source = new FixedLengthSeekableStream(length: 32L * 1024 * 1024);

        // when
        using var buffer = EncodeBufferHelpers.CreateEncodeBuffer(source);

        // then
        buffer.Capacity.Should().Be(32 * 1024 * 1024);
    }

    private sealed class NonSeekableStream(long length) : FixedLengthSeekableStream(length)
    {
        public override bool CanSeek => false;
    }

    private class FixedLengthSeekableStream(long length) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
