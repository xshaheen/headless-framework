// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

public sealed class RawBytesSerializerTests
{
    private readonly RawBytesSerializer _serializer = new();

    [Fact]
    public void should_round_trip_bytes_without_changing_length_or_content()
    {
        // given
        var bytes = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();
        using var output = new MemoryStream();

        // when
        _serializer.Serialize(bytes, output);
        var serialized = output.ToArray();
        var roundTripped = _serializer.Deserialize<byte[]>(new MemoryStream(serialized));

        // then
        serialized.Should().HaveCount(bytes.Length);
        serialized.Should().Equal(bytes);
        roundTripped.Should().Equal(bytes);
    }

    [Fact]
    public void should_round_trip_empty_bytes()
    {
        // given
        var bytes = Array.Empty<byte>();
        using var output = new MemoryStream();

        // when
        _serializer.Serialize(bytes, output);
        var roundTripped = _serializer.Deserialize<byte[]>(new MemoryStream(output.ToArray()));

        // then
        output.ToArray().Should().BeEmpty();
        roundTripped.Should().BeEmpty();
    }

    [Fact]
    public void should_support_object_overloads_for_bytes()
    {
        // given
        object bytes = new byte[] { 1, 2, 3, 4 };
        using var output = new MemoryStream();

        // when
        _serializer.Serialize(bytes, output);
        var roundTripped = _serializer.Deserialize(new MemoryStream(output.ToArray()), typeof(byte[]));

        // then
        roundTripped.Should().BeOfType<byte[]>().Which.Should().Equal((byte[])bytes);
    }

    [Fact]
    public void should_reject_non_byte_array_values()
    {
        // given
        using var output = new MemoryStream();

        // when
        var serialize = () => _serializer.Serialize("value", output);
        var deserialize = () => _serializer.Deserialize<string>(new MemoryStream([1, 2, 3]));

        // then
        serialize.Should().Throw<NotSupportedException>().WithMessage("*byte[]*String*");
        deserialize.Should().Throw<NotSupportedException>().WithMessage("*byte[]*String*");
    }

    [Fact]
    public void should_reject_null_byte_array()
    {
        // given
        byte[]? bytes = null;
        using var output = new MemoryStream();

        // when
        var action = () => _serializer.Serialize(bytes, output);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_read_from_a_publicly_visible_memory_stream_via_the_buffer_fast_path()
    {
        // given — a resizable MemoryStream exposes its buffer (TryGetBuffer == true), exercising the segment-copy
        // fast path rather than the seekable pre-size path the byte[]-backed streams above hit
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream();
        stream.Write(payload);
        stream.Position = 0;

        // when
        var result = _serializer.Deserialize<byte[]>(stream);

        // then
        result.Should().Equal(payload);
    }

    [Fact]
    public void should_read_from_a_non_seekable_stream_via_the_copy_to_fallback()
    {
        // given — a non-seekable stream cannot be pre-sized, exercising the CopyTo fallback branch
        var payload = new byte[] { 9, 8, 7, 6 };
        using var stream = new NonSeekableReadStream(payload);

        // when
        var result = _serializer.Deserialize<byte[]>(stream);

        // then
        result.Should().Equal(payload);
    }

    private sealed class NonSeekableReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
