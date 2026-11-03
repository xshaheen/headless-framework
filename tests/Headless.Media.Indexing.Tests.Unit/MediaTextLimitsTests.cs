// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Headless.Media.Indexing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MediaTextLimitsTests : TestBase
{
    [Fact]
    public async Task should_not_buffer_a_seekable_stream()
    {
        // given
        await using var stream = new MemoryStream([1, 2, 3]);

        // when
        var buffered = await MediaTextLimits.BufferIfNotSeekableAsync(stream, contentLength: 3, AbortToken);

        // then
        buffered.Should().BeNull();
    }

    [Fact]
    public async Task should_buffer_a_non_seekable_stream_into_a_rewound_buffer_of_the_reported_size()
    {
        // given
        byte[] content = [1, 2, 3, 4, 5];
        await using var stream = new NonSeekableStream(new MemoryStream(content));

        // when
        await using var buffered = await MediaTextLimits.BufferIfNotSeekableAsync(stream, content.Length, AbortToken);

        // then
        buffered.Should().NotBeNull();
        buffered!.Position.Should().Be(0);
        buffered.Capacity.Should().Be(content.Length);
        buffered.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task should_buffer_a_non_seekable_stream_of_unknown_length()
    {
        // given
        byte[] content = [1, 2, 3];
        await using var stream = new NonSeekableStream(new MemoryStream(content));

        // when
        await using var buffered = await MediaTextLimits.BufferIfNotSeekableAsync(stream, null, AbortToken);

        // then
        buffered!.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task should_refuse_an_oversize_reported_length_before_reading()
    {
        // given
        await using var stream = new UnreadableStream();

        // when
        var act = async () =>
            await MediaTextLimits.BufferIfNotSeekableAsync(stream, MediaTextLimits.MaxBufferedBytes + 1, AbortToken);

        // then: the read would throw NotSupportedException, so this proves no byte was read
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    /// <summary>A non-seekable stream that fails any read.</summary>
    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
