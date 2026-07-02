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
        // given & when
        var blobInfo = new BlobInfo
        {
            BlobKey = "folder/file.txt",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
        };

        // then
        blobInfo.BlobKey.Should().Be("folder/file.txt");
    }

    [Fact]
    public void should_require_created_when_creating_blob_info()
    {
        // given
        var created = DateTimeOffset.UtcNow.AddDays(-1);

        // when
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = created,
            Modified = DateTimeOffset.UtcNow,
        };

        // then
        blobInfo.Created.Should().Be(created);
    }

    [Fact]
    public void should_require_modified_when_creating_blob_info()
    {
        // given
        var modified = DateTimeOffset.UtcNow;

        // when
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = DateTimeOffset.UtcNow.AddDays(-1),
            Modified = modified,
        };

        // then
        blobInfo.Modified.Should().Be(modified);
    }

    [Fact]
    public void should_store_size_when_size_is_set()
    {
        // given & when
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Size = 1024,
        };

        // then
        blobInfo.Size.Should().Be(1024);
    }

    [Fact]
    public void should_store_metadata_when_metadata_is_provided()
    {
        // given
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["contentType"] = "text/plain" };

        // when
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Metadata = metadata,
        };

        // then
        blobInfo.Metadata.Should().NotBeNull();
        blobInfo.Metadata!["contentType"].Should().Be("text/plain");
    }

    [Fact]
    public void should_have_debugger_display_when_inspecting_blob_info()
    {
        // given & when
        var type = typeof(BlobInfo);

        // then
        var attribute = type.GetCustomAttributes(typeof(DebuggerDisplayAttribute), false);
        attribute.Should().ContainSingle();
    }

    #endregion

    #region BlobUploadRequest Tests

    [Fact]
    public void should_store_filename_when_creating_upload_request()
    {
        // given
        var stream = new MemoryStream();

        // when
        var request = new BlobUploadRequest(stream, "document.pdf");

        // then
        request.FileName.Should().Be("document.pdf");
    }

    [Fact]
    public void should_store_stream_when_creating_upload_request()
    {
        // given
        var stream = new MemoryStream([1, 2, 3]);

        // when
        var request = new BlobUploadRequest(stream, "file.bin");

        // then
        request.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public void should_store_metadata_when_creating_upload_request_with_metadata()
    {
        // given
        var stream = new MemoryStream();
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["author"] = "test" };

        // when
        var request = new BlobUploadRequest(stream, "file.txt", metadata);

        // then
        request.Metadata.Should().NotBeNull();
        request.Metadata!["author"].Should().Be("test");
    }

    [Fact]
    public void should_have_null_metadata_when_not_provided_in_upload_request()
    {
        // given
        var stream = new MemoryStream();

        // when
        var request = new BlobUploadRequest(stream, "file.txt");

        // then
        request.Metadata.Should().BeNull();
    }

    #endregion

    #region NextPageResult Tests

    [Fact]
    public void should_return_has_more_true_when_next_page_exists()
    {
        // given & when
        var result = new NextPageResult
        {
            Success = true,
            HasMore = true,
            Blobs = [],
            NextPageFunc = (_, _) => ValueTask.FromResult<INextPageResult>(null!),
        };

        // then
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public void should_return_has_more_false_when_no_next_page()
    {
        // given & when
        var result = new NextPageResult
        {
            Success = true,
            HasMore = false,
            Blobs = [],
            NextPageFunc = null,
        };

        // then
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public void should_store_blobs_collection_when_blobs_are_provided()
    {
        // given
        var blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file1.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
            new()
            {
                BlobKey = "file2.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
        };

        // when
        var result = new NextPageResult
        {
            Success = true,
            HasMore = false,
            Blobs = blobs,
        };

        // then
        result.Blobs.Should().HaveCount(2);
        result.Blobs.Select(b => b.BlobKey).Should().BeEquivalentTo(["file1.txt", "file2.txt"]);
    }

    #endregion

    #region PagedFileListResult Tests

    [Fact]
    public void should_create_empty_result_when_using_empty_static()
    {
        // when
        var result = PagedFileListResult.Empty;

        // then
        result.Blobs.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task should_create_result_with_blobs_when_using_constructor()
    {
        // given
        var blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "test.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
        };

        // when
        await using var result = new PagedFileListResult(blobs);

        // then
        result.Blobs.Should().ContainSingle();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task should_navigate_to_next_page_when_next_page_func_provided()
    {
        // given
        var page1Blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file1.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
        };

        var page2Blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file2.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
        };

        await using var result = new PagedFileListResult(
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

        // when
        var hasNext = await result.NextPageAsync(AbortToken);

        // then
        hasNext.Should().BeTrue();
        result.Blobs.Should().ContainSingle();
        result.Blobs.First().BlobKey.Should().Be("file2.txt");
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_when_no_next_page_func()
    {
        // given
        var blobs = new List<BlobInfo>();
        await using var result = new PagedFileListResult(blobs);

        // when
        var hasNext = await result.NextPageAsync(AbortToken);

        // then
        hasNext.Should().BeFalse();
    }

    [Fact]
    public async Task should_handle_failed_next_page_when_success_is_false()
    {
        // given
        var initialBlobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file1.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
        };

        await using var result = new PagedFileListResult(
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

        // when
        var hasNext = await result.NextPageAsync(AbortToken);

        // then
        hasNext.Should().BeFalse();
        result.Blobs.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    #endregion
}
