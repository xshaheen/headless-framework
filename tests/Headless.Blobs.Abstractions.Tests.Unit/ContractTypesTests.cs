// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Blobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ContractTypesTests : TestBase
{
    #region BlobInfo Tests

    [Fact]
    public void should_require_blob_key_when_creating_blob_info()
    {
        // Arrange & Act
        var blobInfo = new BlobInfo
        {
            BlobKey = "folder/file.txt",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
        };

        // Assert
        blobInfo.BlobKey.Should().Be("folder/file.txt");
    }

    [Fact]
    public void should_require_created_when_creating_blob_info()
    {
        // Arrange
        var created = DateTimeOffset.UtcNow.AddDays(-1);

        // Act
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = created,
            Modified = DateTimeOffset.UtcNow,
        };

        // Assert
        blobInfo.Created.Should().Be(created);
    }

    [Fact]
    public void should_require_modified_when_creating_blob_info()
    {
        // Arrange
        var modified = DateTimeOffset.UtcNow;

        // Act
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = DateTimeOffset.UtcNow.AddDays(-1),
            Modified = modified,
        };

        // Assert
        blobInfo.Modified.Should().Be(modified);
    }

    [Fact]
    public void should_store_size_when_size_is_set()
    {
        // Arrange & Act
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Size = 1024,
        };

        // Assert
        blobInfo.Size.Should().Be(1024);
    }

    [Fact]
    public void should_store_metadata_when_metadata_is_provided()
    {
        // Arrange
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["contentType"] = "text/plain" };

        // Act
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Metadata = metadata,
        };

        // Assert
        blobInfo.Metadata.Should().NotBeNull();
        blobInfo.Metadata!["contentType"].Should().Be("text/plain");
    }

    [Fact]
    public void should_have_debugger_display_when_inspecting_blob_info()
    {
        // Arrange & Act
        var type = typeof(BlobInfo);

        // Assert
        var attribute = type.GetCustomAttributes(typeof(DebuggerDisplayAttribute), false);
        attribute.Should().HaveCount(1);
    }

    #endregion

    #region BlobUploadRequest Tests

    [Fact]
    public void should_store_filename_when_creating_upload_request()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var request = new BlobUploadRequest(stream, "document.pdf");

        // Assert
        request.FileName.Should().Be("document.pdf");
    }

    [Fact]
    public void should_store_stream_when_creating_upload_request()
    {
        // Arrange
        var stream = new MemoryStream([1, 2, 3]);

        // Act
        var request = new BlobUploadRequest(stream, "file.bin");

        // Assert
        request.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public void should_store_metadata_when_creating_upload_request_with_metadata()
    {
        // Arrange
        var stream = new MemoryStream();
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["author"] = "test" };

        // Act
        var request = new BlobUploadRequest(stream, "file.txt", metadata);

        // Assert
        request.Metadata.Should().NotBeNull();
        request.Metadata!["author"].Should().Be("test");
    }

    [Fact]
    public void should_have_null_metadata_when_not_provided_in_upload_request()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var request = new BlobUploadRequest(stream, "file.txt");

        // Assert
        request.Metadata.Should().BeNull();
    }

    #endregion

    #region NextPageResult Tests

    [Fact]
    public void should_return_has_more_true_when_next_page_exists()
    {
        // Arrange & Act
        var result = new NextPageResult
        {
            Success = true,
            HasMore = true,
            Blobs = [],
            NextPageFunc = (_, _) => ValueTask.FromResult<INextPageResult>(null!),
        };

        // Assert
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public void should_return_has_more_false_when_no_next_page()
    {
        // Arrange & Act
        var result = new NextPageResult
        {
            Success = true,
            HasMore = false,
            Blobs = [],
            NextPageFunc = null,
        };

        // Assert
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public void should_store_blobs_collection_when_blobs_are_provided()
    {
        // Arrange
        var blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file1.txt",
                Created = DateTimeOffset.Now,
                Modified = DateTimeOffset.Now,
            },
            new()
            {
                BlobKey = "file2.txt",
                Created = DateTimeOffset.Now,
                Modified = DateTimeOffset.Now,
            },
        };

        // Act
        var result = new NextPageResult
        {
            Success = true,
            HasMore = false,
            Blobs = blobs,
        };

        // Assert
        result.Blobs.Should().HaveCount(2);
        result.Blobs.Select(b => b.BlobKey).Should().BeEquivalentTo(["file1.txt", "file2.txt"]);
    }

    #endregion

    #region PagedFileListResult Tests

    [Fact]
    public void should_create_empty_result_when_using_empty_static()
    {
        // Act
        var result = PagedFileListResult.Empty;

        // Assert
        result.Blobs.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public void should_create_result_with_blobs_when_using_constructor()
    {
        // Arrange
        var blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "test.txt",
                Created = DateTimeOffset.Now,
                Modified = DateTimeOffset.Now,
            },
        };

        // Act
        var result = new PagedFileListResult(blobs);

        // Assert
        result.Blobs.Should().HaveCount(1);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task should_navigate_to_next_page_when_next_page_func_provided()
    {
        // Arrange
        var page1Blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file1.txt",
                Created = DateTimeOffset.Now,
                Modified = DateTimeOffset.Now,
            },
        };

        var page2Blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file2.txt",
                Created = DateTimeOffset.Now,
                Modified = DateTimeOffset.Now,
            },
        };

        var result = new PagedFileListResult(
            page1Blobs,
            hasMore: true,
            (_, _) =>
                ValueTask.FromResult<INextPageResult>(
                    new NextPageResult
                    {
                        Success = true,
                        HasMore = false,
                        Blobs = page2Blobs,
                    }
                )
        );

        // Act
        var hasNext = await result.NextPageAsync(AbortToken);

        // Assert
        hasNext.Should().BeTrue();
        result.Blobs.Should().HaveCount(1);
        result.Blobs.First().BlobKey.Should().Be("file2.txt");
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_when_no_next_page_func()
    {
        // Arrange
        var blobs = new List<BlobInfo>();
        var result = new PagedFileListResult(blobs);

        // Act
        var hasNext = await result.NextPageAsync(AbortToken);

        // Assert
        hasNext.Should().BeFalse();
    }

    [Fact]
    public async Task should_handle_failed_next_page_when_success_is_false()
    {
        // Arrange
        var initialBlobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file1.txt",
                Created = DateTimeOffset.Now,
                Modified = DateTimeOffset.Now,
            },
        };

        var result = new PagedFileListResult(
            initialBlobs,
            hasMore: true,
            (_, _) =>
                ValueTask.FromResult<INextPageResult>(
                    new NextPageResult
                    {
                        Success = false,
                        HasMore = false,
                        Blobs = [],
                    }
                )
        );

        // Act
        var hasNext = await result.NextPageAsync(AbortToken);

        // Assert
        hasNext.Should().BeFalse();
        result.Blobs.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    #endregion
}
