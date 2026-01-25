// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;
using Framework.Testing.Tests;

namespace Tests;

public sealed class BlobDownloadResultTests : TestBase
{
    #region Property Access Tests

    [Fact]
    public void should_expose_stream_property_when_created_with_stream()
    {
        // Arrange
        using var stream = new MemoryStream([1, 2, 3]);

        // Act
        using var result = new BlobDownloadResult(stream, "test.txt");

        // Assert
        result.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public void should_expose_filename_property_when_created_with_filename()
    {
        // Arrange
        using var stream = new MemoryStream();
        var fileName = "document.pdf";

        // Act
        using var result = new BlobDownloadResult(stream, fileName);

        // Assert
        result.FileName.Should().Be(fileName);
    }

    [Fact]
    public void should_expose_metadata_property_when_created_with_metadata()
    {
        // Arrange
        using var stream = new MemoryStream();
        var metadata = new Dictionary<string, string?> { ["key"] = "value", ["author"] = "test" };

        // Act
        using var result = new BlobDownloadResult(stream, "file.txt", metadata);

        // Assert
        result.Metadata.Should().NotBeNull();
        result.Metadata!["key"].Should().Be("value");
        result.Metadata["author"].Should().Be("test");
    }

    [Fact]
    public void should_have_null_metadata_when_not_provided()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        using var result = new BlobDownloadResult(stream, "file.txt");

        // Assert
        result.Metadata.Should().BeNull();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void should_dispose_stream_when_dispose_is_called()
    {
        // Arrange
        var stream = new MemoryStream([1, 2, 3]);
        var result = new BlobDownloadResult(stream, "test.txt");

        // Act
        result.Dispose();

        // Assert - verify stream is disposed by trying to read
        var act = () => stream.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_dispose_stream_async_when_dispose_async_is_called()
    {
        // Arrange
        var stream = new MemoryStream([1, 2, 3]);
        var result = new BlobDownloadResult(stream, "test.txt");

        // Act
        await result.DisposeAsync();

        // Assert - verify stream is disposed by trying to read
        var act = () => stream.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion

    #region Using Statement Tests

    [Fact]
    public void should_support_using_statement_when_used_with_using_block()
    {
        // Arrange
        var stream = new MemoryStream([1, 2, 3]);
        Stream? capturedStream = null;

        // Act
        using (var result = new BlobDownloadResult(stream, "test.txt"))
        {
            capturedStream = result.Stream;
            capturedStream.ReadByte().Should().Be(1); // Stream should be readable
        }

        // Assert - stream should be disposed after using block
        var act = () => capturedStream.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_support_await_using_statement_when_used_with_await_using()
    {
        // Arrange
        var stream = new MemoryStream([1, 2, 3]);
        Stream? capturedStream = null;

        // Act
        await using (var result = new BlobDownloadResult(stream, "test.txt"))
        {
            capturedStream = result.Stream;
            capturedStream.ReadByte().Should().Be(1); // Stream should be readable
        }

        // Assert - stream should be disposed after await using block
        var act = () => capturedStream.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion
}
