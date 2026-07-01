// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlobStorageExtensionsTests : TestBase
{
    private readonly IBlobStorage _storage = Substitute.For<IBlobStorage>();

    protected override async ValueTask DisposeAsyncCore()
    {
        await _storage.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    #region UploadAsync (with BlobUploadRequest) Tests

    [Fact]
    public async Task should_delegate_to_core_upload_when_using_request_object()
    {
        // given
        string[] container = ["bucket", "uploads"];
        var stream = new MemoryStream([1, 2, 3]);
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["key"] = "value" };
        var request = new BlobUploadRequest(stream, "test.txt", metadata);

        // when
        await _storage.UploadAsync(container, request, AbortToken);

        // then
        await _storage.Received(1).UploadAsync(container, "test.txt", stream, metadata, AbortToken);
    }

    [Fact]
    public async Task should_pass_metadata_from_request_when_metadata_is_provided()
    {
        // given
        string[] container = ["bucket"];
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["contentType"] = "text/plain",
            ["author"] = "test",
        };
        var request = new BlobUploadRequest(new MemoryStream(), "file.txt", metadata);

        // when
        await _storage.UploadAsync(container, request, AbortToken);

        // then
        await _storage
            .Received(1)
            .UploadAsync(
                container,
                "file.txt",
                Arg.Any<Stream>(),
                Arg.Is<Dictionary<string, string?>>(m => m["contentType"] == "text/plain" && m["author"] == "test"),
                AbortToken
            );
    }

    #endregion

    #region GetBlobsListAsync Tests

    [Fact]
    public async Task should_collect_all_pages_when_iterating_through_results()
    {
        // given
        string[] container = ["bucket"];

        var page1Blobs = new List<BlobInfo>
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

        var page2Blobs = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file3.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
        };

        await using var page1Result = new PagedFileListResult(
            page1Blobs,
            hasMore: true,
            (_, _) =>
                ValueTask.FromResult<INextPageResult>(
                    new NextPageResult
                    {
                        Success = true,
                        HasMore = false,
                        Blobs = page2Blobs,
                        NextPageFunc = null,
                    }
                )
        );

        _storage.GetPagedListAsync(container, null, Arg.Any<int>(), AbortToken).Returns(page1Result);

        // when
        var result = await _storage.GetBlobsListAsync(container, cancellationToken: AbortToken);

        // then
        result.Should().HaveCount(3);
        result.Select(b => b.BlobKey).Should().BeEquivalentTo(["file1.txt", "file2.txt", "file3.txt"]);
    }

    [Fact]
    public async Task should_respect_limit_parameter_when_limit_is_set()
    {
        // given
        string[] container = ["bucket"];

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
            new()
            {
                BlobKey = "file3.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
            },
        };

        await using var pageResult = new PagedFileListResult(
            blobs,
            hasMore: true,
            (_, _) =>
                ValueTask.FromResult<INextPageResult>(
                    new NextPageResult
                    {
                        Success = true,
                        HasMore = true,
                        Blobs = blobs,
                    }
                )
        );

        _storage.GetPagedListAsync(container, null, 2, AbortToken).Returns(pageResult);

        // when
        var result = await _storage.GetBlobsListAsync(container, limit: 2, cancellationToken: AbortToken);

        // then - first page returns 3 but limit stops further pages
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_use_default_limit_of_1m_when_limit_not_specified()
    {
        // given
        string[] container = ["bucket"];
        await using var pageResult = new PagedFileListResult([]);

        _storage.GetPagedListAsync(container, null, 1_000_000, AbortToken).Returns(pageResult);

        // when
        await _storage.GetBlobsListAsync(container, cancellationToken: AbortToken);

        // then
        await _storage.Received(1).GetPagedListAsync(container, null, 1_000_000, AbortToken);
    }

    #endregion

    #region UploadContentAsync (string) Tests

    [Fact]
    public async Task should_upload_string_content_when_content_is_provided()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "test.txt";
        const string contents = "Hello, World!";

        // when
        await _storage.UploadContentAsync(container, blobName, contents, AbortToken);

        // then
        await _storage.Received(1).UploadAsync(container, blobName, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_handle_null_content_when_string_is_null()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "test.txt";
        const string? contents = null;

        // when
        await _storage.UploadContentAsync(container, blobName, contents, AbortToken);

        // then
        await _storage.Received(1).UploadAsync(container, blobName, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_pass_metadata_when_uploading_string_with_metadata()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "test.txt";
        const string contents = "content";
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["type"] = "text" };

        // when
        await _storage.UploadContentAsync(container, blobName, contents, metadata, AbortToken);

        // then
        await _storage
            .Received(1)
            .UploadAsync(
                container,
                blobName,
                Arg.Any<Stream>(),
                Arg.Is<Dictionary<string, string?>>(m => m["type"] == "text"),
                AbortToken
            );
    }

    #endregion

    #region UploadContentAsync<T> (JSON) Tests

    [Fact]
    public async Task should_serialize_object_to_json_when_uploading_typed_content()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "data.json";
        var contents = new TestData { Name = "Test", Value = 42 };

        // when
        await _storage.UploadContentAsync(container, blobName, contents, AbortToken);

        // then
        await _storage.Received(1).UploadAsync(container, blobName, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_handle_null_object_when_uploading_null_typed_content()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "data.json";
        TestData? contents = null;

        // when
        await _storage.UploadContentAsync(container, blobName, contents, AbortToken);

        // then
        await _storage.Received(1).UploadAsync(container, blobName, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_use_provided_options_when_custom_json_options_provided()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "data.json";
        var contents = new TestData { Name = "Test", Value = 42 };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // when
        await _storage.UploadContentAsync(container, blobName, contents, options, AbortToken);

        // then
        await _storage.Received(1).UploadAsync(container, blobName, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_use_json_type_info_for_aot_when_type_info_provided()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "data.json";
        var contents = new TestData { Name = "AOT Test", Value = 100 };

        // when
        await _storage.UploadContentAsync(
            container,
            blobName,
            contents,
            TestDataJsonContext.Default.TestData,
            AbortToken
        );

        // then
        await _storage.Received(1).UploadAsync(container, blobName, Arg.Any<Stream>(), null, AbortToken);
    }

    #endregion

    #region GetBlobContentAsync Tests

    [Fact]
    public async Task should_return_null_when_blob_not_found()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "nonexistent.txt";

        _storage.OpenReadStreamAsync(container, blobName, AbortToken).Returns((BlobDownloadResult?)null);

        // when
        var result = await _storage.GetBlobContentAsync(container, blobName, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_read_string_content_when_blob_exists()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "test.txt";
        const string expectedContent = "Hello, World!";

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(expectedContent));
        await using var downloadResult = new BlobDownloadResult(stream, blobName);

        _storage.OpenReadStreamAsync(container, blobName, AbortToken).Returns(downloadResult);

        // when
        var result = await _storage.GetBlobContentAsync(container, blobName, AbortToken);

        // then
        result.Should().Be(expectedContent);
    }

    #endregion

    #region GetBlobContentAsync<T> (JSON) Tests

    [Fact]
    public async Task should_deserialize_json_content_when_blob_contains_json()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "data.json";
        const string json = """{"Name":"Deserialized","Value":99}""";

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await using var downloadResult = new BlobDownloadResult(stream, blobName);

        _storage.OpenReadStreamAsync(container, blobName, AbortToken).Returns(downloadResult);

        // when
        var result = await _storage.GetBlobContentAsync<TestData>(container, blobName, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be("Deserialized");
        result.Value.Should().Be(99);
    }

    [Fact]
    public async Task should_return_default_when_blob_not_found_for_typed_content()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "nonexistent.json";

        _storage.OpenReadStreamAsync(container, blobName, AbortToken).Returns((BlobDownloadResult?)null);

        // when
        var result = await _storage.GetBlobContentAsync<TestData>(container, blobName, cancellationToken: AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_use_json_type_info_for_aot_deserialization_when_type_info_provided()
    {
        // given
        string[] container = ["bucket"];
        const string blobName = "data.json";
        const string json = """{"Name":"AOT","Value":123}""";

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await using var downloadResult = new BlobDownloadResult(stream, blobName);

        _storage.OpenReadStreamAsync(container, blobName, AbortToken).Returns(downloadResult);

        // when
        var result = await _storage.GetBlobContentAsync(
            container,
            blobName,
            TestDataJsonContext.Default.TestData,
            AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be("AOT");
        result.Value.Should().Be(123);
    }

    #endregion
}

public sealed class TestData
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

[JsonSerializable(typeof(TestData))]
internal sealed partial class TestDataJsonContext : JsonSerializerContext;
