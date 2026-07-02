// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlobDownloadResultTests : TestBase
{
    #region Property Access Tests

    [Fact]
    public void should_expose_stream_property_when_created_with_stream()
    {
        // given
        using var stream = new MemoryStream([1, 2, 3]);

        // when
        using var result = new BlobDownloadResult(stream, "test.txt");

        // then
        result.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public void should_expose_filename_property_when_created_with_filename()
    {
        // given
        using var stream = new MemoryStream();
        const string fileName = "document.pdf";

        // when
        using var result = new BlobDownloadResult(stream, fileName);

        // then
        result.FileName.Should().Be(fileName);
    }

    [Fact]
    public void should_expose_metadata_property_when_created_with_metadata()
    {
        // given
        using var stream = new MemoryStream();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = "value",
            ["author"] = "test",
        };

        // when
        using var result = new BlobDownloadResult(stream, "file.txt", metadata);

        // then
        result.Metadata.Should().NotBeNull();
        result.Metadata!["key"].Should().Be("value");
        result.Metadata["author"].Should().Be("test");
    }

    [Fact]
    public void should_have_null_metadata_when_not_provided()
    {
        // given
        using var stream = new MemoryStream();

        // when
        using var result = new BlobDownloadResult(stream, "file.txt");

        // then
        result.Metadata.Should().BeNull();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void should_dispose_stream_when_dispose_is_called()
    {
        // given
        var stream = new MemoryStream([1, 2, 3]);
        var result = new BlobDownloadResult(stream, "test.txt");

        // when
        result.Dispose();

        // then - verify stream is disposed by trying to read
        var act = stream.ReadByte;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_dispose_stream_async_when_dispose_async_is_called()
    {
        // given
        var stream = new MemoryStream([1, 2, 3]);
        var result = new BlobDownloadResult(stream, "test.txt");

        // when
        await result.DisposeAsync();

        // then - verify stream is disposed by trying to read
        var act = stream.ReadByte;
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion

    #region Using Statement Tests

    [Fact]
    public void should_support_using_statement_when_used_with_using_block()
    {
        // given
        var stream = new MemoryStream([1, 2, 3]);
        Stream? capturedStream;

        // when
        using (var result = new BlobDownloadResult(stream, "test.txt"))
        {
            capturedStream = result.Stream;
            capturedStream.ReadByte().Should().Be(1); // Stream should be readable
        }

        // then - stream should be disposed after using block
        var act = () => capturedStream.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_support_await_using_statement_when_used_with_await_using()
    {
        // given
        var stream = new MemoryStream([1, 2, 3]);
        Stream? capturedStream;

        // when
        await using (var result = new BlobDownloadResult(stream, "test.txt"))
        {
            capturedStream = result.Stream;
            capturedStream.ReadByte().Should().Be(1); // Stream should be readable
        }

        // then - stream should be disposed after await using block
        var act = () => capturedStream.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion
}
