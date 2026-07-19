// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Headless.Testing.Tests;

namespace Tests.IO;

public sealed class SizeLimitedReadStreamTests : TestBase
{
    [Fact]
    public void should_reject_null_unreadable_and_negative_inputs()
    {
        var nullStream = () => new SizeLimitedReadStream(null!, 1);
        using var unreadableStream = new MemoryStream();
        unreadableStream.Dispose();
        var unreadable = () => new SizeLimitedReadStream(unreadableStream, 1);
        var negativeLimit = () => new SizeLimitedReadStream(Stream.Null, -1);

        nullStream.Should().Throw<ArgumentNullException>();
        unreadable.Should().Throw<ArgumentException>();
        negativeLimit.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_allow_content_at_the_byte_limit()
    {
        await using var source = new MemoryStream(new byte[16]);
        await using var sut = new SizeLimitedReadStream(source, 16, leaveOpen: true);

        await sut.CopyToAsync(Stream.Null, AbortToken);

        sut.BytesRead.Should().Be(16);
        sut.MaximumBytes.Should().Be(16);
    }

    [Fact]
    public async Task should_throw_when_async_content_exceeds_the_byte_limit()
    {
        await using var source = new MemoryStream(new byte[17]);
        await using var sut = new SizeLimitedReadStream(source, 16, leaveOpen: true);

        var act = async () => await sut.CopyToAsync(Stream.Null, AbortToken);

        var exception = await act.Should().ThrowAsync<StreamSizeLimitExceededException>();
        exception.Which.MaximumBytes.Should().Be(16);
        exception.Which.BytesRead.Should().Be(17);
    }

    [Fact]
    public void should_throw_when_sync_content_exceeds_the_byte_limit()
    {
        using var source = new MemoryStream(new byte[5]);
        using var sut = new SizeLimitedReadStream(source, 4, leaveOpen: true);
        var buffer = new byte[10];

        var act = () => sut.Read(buffer);

        act.Should().Throw<StreamSizeLimitExceededException>();
        source.Position.Should().Be(5);
    }

    [Fact]
    public void should_probe_at_most_one_byte_beyond_the_limit()
    {
        using var source = new MemoryStream(new byte[100]);
        using var sut = new SizeLimitedReadStream(source, 4, leaveOpen: true);
        var buffer = new byte[100];

        var act = () => sut.Read(buffer);

        act.Should().Throw<StreamSizeLimitExceededException>();
        source.Position.Should().Be(5);
        sut.BytesRead.Should().Be(5);
    }

    [Fact]
    public void should_rethrow_without_reading_again_after_limit_is_exceeded()
    {
        using var source = new MemoryStream(new byte[10]);
        using var sut = new SizeLimitedReadStream(source, 2, leaveOpen: true);
        var buffer = new byte[10];

        var first = () => sut.Read(buffer);
        first.Should().Throw<StreamSizeLimitExceededException>();
        var positionAfterOverflow = source.Position;

        var second = () => sut.Read(buffer);

        second.Should().Throw<StreamSizeLimitExceededException>();
        source.Position.Should().Be(positionAfterOverflow);
    }

    [Fact]
    public void should_leave_inner_stream_open_when_configured()
    {
        using var source = new MemoryStream([1]);
        var sut = new SizeLimitedReadStream(source, 1, leaveOpen: true);

        sut.Dispose();

        source.ReadByte().Should().Be(1);
    }

    [Fact]
    public void should_dispose_inner_stream_by_default()
    {
        var source = new MemoryStream([1]);
        var sut = new SizeLimitedReadStream(source, 1);

        sut.Dispose();

        var act = () => source.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }
}
