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

    private static BlobInfo _Blob(string key) =>
        new()
        {
            BlobKey = key,
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Size = 0,
        };

    #region GetBlobsAsync (streaming) Tests

    [Fact]
    public async Task should_stream_across_pages_when_continuation_token_present()
    {
        // Arrange
        var page1 = new BlobPage([_Blob("file1.txt"), _Blob("file2.txt")], "page-2");
        var page2 = new BlobPage([_Blob("file3.txt")], null);

        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(page1);
        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == "page-2"), Arg.Any<CancellationToken>())
            .Returns(page2);

        // Act
        var collected = new List<BlobInfo>();
        await foreach (var blob in _storage.GetBlobsAsync(new BlobQuery("bucket"), AbortToken))
        {
            collected.Add(blob);
        }

        // Assert
        collected.Select(b => b.BlobKey).Should().BeEquivalentTo(["file1.txt", "file2.txt", "file3.txt"]);
    }

    [Fact]
    public async Task should_carry_prefix_and_page_size_into_next_page_query()
    {
        // Arrange
        var page1 = new BlobPage([_Blob("logs/file1.txt")], "page-2");
        var page2 = new BlobPage([_Blob("logs/file2.txt")], null);

        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(page1);
        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == "page-2"), Arg.Any<CancellationToken>())
            .Returns(page2);

        // Act
        await foreach (var _ in _storage.GetBlobsAsync(new BlobQuery("bucket", "logs/", pageSize: 50), AbortToken)) { }

        // Assert - the second page request preserves prefix + page size
        await _storage
            .Received(1)
            .ListAsync(
                Arg.Is<BlobQuery>(q =>
                    q.ContinuationToken == "page-2"
                    && q.Prefix == "logs/"
                    && q.PageSize == 50
                    && q.Container == "bucket"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region GetBlobsAsync (glob filter) Tests

    [Fact]
    public async Task should_filter_to_matching_keys_when_glob_pattern_provided()
    {
        // Arrange
        var page = new BlobPage([_Blob("a.txt"), _Blob("b.json"), _Blob("c.txt")], null);
        _storage.ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        // Act
        var collected = new List<BlobInfo>();
        await foreach (var blob in _storage.GetBlobsAsync(new BlobQuery("bucket"), "*.txt", AbortToken))
        {
            collected.Add(blob);
        }

        // Assert
        collected.Select(b => b.BlobKey).Should().BeEquivalentTo(["a.txt", "c.txt"]);
    }

    [Fact]
    public async Task should_push_glob_literal_prefix_when_it_narrows_query()
    {
        // Arrange
        var page = new BlobPage([_Blob("logs/2026/a.txt"), _Blob("logs/2026/b.json")], null);
        _storage.ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        // Act
        var collected = new List<BlobInfo>();
        await foreach (
            var blob in _storage.GetBlobsAsync(new BlobQuery("bucket", "logs/"), "logs/2026/*.txt", AbortToken)
        )
        {
            collected.Add(blob);
        }

        // Assert
        collected.Select(b => b.BlobKey).Should().BeEquivalentTo(["logs/2026/a.txt"]);
        await _storage
            .Received(1)
            .ListAsync(
                Arg.Is<BlobQuery>(q => q.Container == "bucket" && q.Prefix == "logs/2026/" && q.PageSize == 100),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_keep_query_prefix_when_it_is_already_narrower_than_glob_literal_prefix()
    {
        // Arrange
        var page = new BlobPage([_Blob("logs/2026/a.txt")], null);
        _storage.ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        // Act
        await foreach (var _ in _storage.GetBlobsAsync(new BlobQuery("bucket", "logs/2026/"), "logs/*.txt", AbortToken))
        { }

        // Assert
        await _storage
            .Received(1)
            .ListAsync(
                Arg.Is<BlobQuery>(q => q.Container == "bucket" && q.Prefix == "logs/2026/"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_enumerate_when_query_prefix_and_glob_literal_prefix_are_incompatible()
    {
        // Act
        var collected = new List<BlobInfo>();
        await foreach (var blob in _storage.GetBlobsAsync(new BlobQuery("bucket", "logs/"), "images/*.txt", AbortToken))
        {
            collected.Add(blob);
        }

        // Assert
        collected.Should().BeEmpty();
        await _storage.DidNotReceiveWithAnyArgs().ListAsync(default!, AbortToken);
    }

    [Fact]
    public async Task should_not_change_prefix_when_query_has_continuation_token()
    {
        // Arrange
        var page = new BlobPage([_Blob("logs/2026/a.txt")], null);
        _storage.ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        // Act
        await foreach (
            var _ in _storage.GetBlobsAsync(
                new BlobQuery("bucket", "logs/", continuationToken: "token-1"),
                "logs/2026/*.txt",
                AbortToken
            )
        ) { }

        // Assert
        await _storage
            .Received(1)
            .ListAsync(
                Arg.Is<BlobQuery>(q => q.Prefix == "logs/" && q.ContinuationToken == "token-1"),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region GetBlobsListAsync Tests

    [Fact]
    public async Task should_collect_all_pages_when_materializing_list()
    {
        // Arrange
        var page1 = new BlobPage([_Blob("file1.txt"), _Blob("file2.txt")], "page-2");
        var page2 = new BlobPage([_Blob("file3.txt")], null);

        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(page1);
        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == "page-2"), Arg.Any<CancellationToken>())
            .Returns(page2);

        // Act
        var result = await _storage.GetBlobsListAsync(new BlobQuery("bucket"), cancellationToken: AbortToken);

        // Assert
        result.Select(b => b.BlobKey).Should().BeEquivalentTo(["file1.txt", "file2.txt", "file3.txt"]);
    }

    [Fact]
    public async Task should_respect_limit_when_materializing_list()
    {
        // Arrange
        var page1 = new BlobPage([_Blob("file1.txt"), _Blob("file2.txt")], "page-2");
        var page2 = new BlobPage([_Blob("file3.txt")], null);

        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(page1);
        _storage
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == "page-2"), Arg.Any<CancellationToken>())
            .Returns(page2);

        // Act
        var result = await _storage.GetBlobsListAsync(new BlobQuery("bucket"), limit: 2, cancellationToken: AbortToken);

        // Assert - stops at the limit without fetching the second page
        result.Should().HaveCount(2);
        await _storage
            .DidNotReceive()
            .ListAsync(Arg.Is<BlobQuery>(q => q.ContinuationToken == "page-2"), Arg.Any<CancellationToken>());
    }

    #endregion

    #region UploadContentAsync (string) Tests

    [Fact]
    public async Task should_upload_string_content_when_content_is_provided()
    {
        // Arrange
        var location = new BlobLocation("bucket", "test.txt");
        const string contents = "Hello, World!";

        // Act
        await _storage.UploadContentAsync(location, contents, AbortToken);

        // Assert
        await _storage.Received(1).UploadAsync(location, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_handle_null_content_when_string_is_null()
    {
        // Arrange
        var location = new BlobLocation("bucket", "test.txt");
        const string? contents = null;

        // Act
        await _storage.UploadContentAsync(location, contents, AbortToken);

        // Assert
        await _storage.Received(1).UploadAsync(location, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_pass_metadata_when_uploading_string_with_metadata()
    {
        // Arrange
        var location = new BlobLocation("bucket", "test.txt");
        const string contents = "content";
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = "text" };

        // Act
        await _storage.UploadContentAsync(location, contents, metadata, AbortToken);

        // Assert
        await _storage
            .Received(1)
            .UploadAsync(
                location,
                Arg.Any<Stream>(),
                Arg.Is<IReadOnlyDictionary<string, string>>(m => m["type"] == "text"),
                AbortToken
            );
    }

    #endregion

    #region UploadContentAsync<T> (JSON) Tests

    [Fact]
    public async Task should_serialize_object_to_json_when_uploading_typed_content()
    {
        // Arrange
        var location = new BlobLocation("bucket", "data.json");
        var contents = new TestData { Name = "Test", Value = 42 };

        // Act
        await _storage.UploadContentAsync(location, contents, AbortToken);

        // Assert
        await _storage.Received(1).UploadAsync(location, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_handle_null_object_when_uploading_null_typed_content()
    {
        // Arrange
        var location = new BlobLocation("bucket", "data.json");
        TestData? contents = null;

        // Act
        await _storage.UploadContentAsync(location, contents, AbortToken);

        // Assert
        await _storage.Received(1).UploadAsync(location, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_use_provided_options_when_custom_json_options_provided()
    {
        // Arrange
        var location = new BlobLocation("bucket", "data.json");
        var contents = new TestData { Name = "Test", Value = 42 };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        await _storage.UploadContentAsync(location, contents, options, AbortToken);

        // Assert
        await _storage.Received(1).UploadAsync(location, Arg.Any<Stream>(), null, AbortToken);
    }

    [Fact]
    public async Task should_use_json_type_info_for_aot_when_type_info_provided()
    {
        // Arrange
        var location = new BlobLocation("bucket", "data.json");
        var contents = new TestData { Name = "AOT Test", Value = 100 };

        // Act
        await _storage.UploadContentAsync(location, contents, TestDataJsonContext.Default.TestData, AbortToken);

        // Assert
        await _storage.Received(1).UploadAsync(location, Arg.Any<Stream>(), null, AbortToken);
    }

    #endregion

    #region GetBlobContentAsync Tests

    [Fact]
    public async Task should_return_null_when_blob_not_found()
    {
        // Arrange
        var location = new BlobLocation("bucket", "nonexistent.txt");

        // ReSharper disable once NotDisposedResource
        _storage.OpenReadStreamAsync(location, AbortToken).Returns((BlobDownloadResult?)null);

        // Act
        var result = await _storage.GetBlobContentAsync(location, AbortToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_read_string_content_when_blob_exists()
    {
        // Arrange
        var location = new BlobLocation("bucket", "test.txt");
        const string expectedContent = "Hello, World!";

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(expectedContent));
        await using var downloadResult = new BlobDownloadResult(stream, "test.txt");

        // ReSharper disable once NotDisposedResource
        _storage.OpenReadStreamAsync(location, AbortToken).Returns(downloadResult);

        // Act
        var result = await _storage.GetBlobContentAsync(location, AbortToken);

        // Assert
        result.Should().Be(expectedContent);
    }

    #endregion

    #region UploadContentAsync / GetBlobContentAsync round-trip Tests

    [Fact]
    public async Task should_round_trip_string_content_through_in_memory_storage()
    {
        // Arrange
        await using var storage = new InMemoryBlobStorage();
        var location = new BlobLocation("bucket", "round/trip.txt");
        const string contents = "Round-trip UTF-8 ✓";

        // Act
        await storage.UploadContentAsync(location, contents, AbortToken);
        var read = await storage.GetBlobContentAsync(location, AbortToken);

        // Assert
        read.Should().Be(contents);
    }

    [Fact]
    public async Task should_round_trip_json_content_through_in_memory_storage()
    {
        // Arrange
        await using var storage = new InMemoryBlobStorage();
        var location = new BlobLocation("bucket", "round/data.json");
        var contents = new TestData { Name = "RoundTrip", Value = 7 };

        // Act
        await storage.UploadContentAsync(location, contents, TestDataJsonContext.Default.TestData, AbortToken);
        var read = await storage.GetBlobContentAsync(location, TestDataJsonContext.Default.TestData, AbortToken);

        // Assert
        read.Should().NotBeNull();
        read!.Name.Should().Be("RoundTrip");
        read.Value.Should().Be(7);
    }

    #endregion

    #region GetBlobContentAsync<T> (JSON) Tests

    [Fact]
    public async Task should_deserialize_json_content_when_blob_contains_json()
    {
        // Arrange
        var location = new BlobLocation("bucket", "data.json");
        const string json = """{"Name":"Deserialized","Value":99}""";

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await using var downloadResult = new BlobDownloadResult(stream, "data.json");

        // ReSharper disable once NotDisposedResource
        _storage.OpenReadStreamAsync(location, AbortToken).Returns(downloadResult);

        // Act
        var result = await _storage.GetBlobContentAsync<TestData>(location, cancellationToken: AbortToken);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Deserialized");
        result.Value.Should().Be(99);
    }

    [Fact]
    public async Task should_return_default_when_blob_not_found_for_typed_content()
    {
        // Arrange
        var location = new BlobLocation("bucket", "nonexistent.json");

        // ReSharper disable once NotDisposedResource
        _storage.OpenReadStreamAsync(location, AbortToken).Returns((BlobDownloadResult?)null);

        // Act
        var result = await _storage.GetBlobContentAsync<TestData>(location, cancellationToken: AbortToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_use_json_type_info_for_aot_deserialization_when_type_info_provided()
    {
        // Arrange
        var location = new BlobLocation("bucket", "data.json");
        const string json = """{"Name":"AOT","Value":123}""";

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await using var downloadResult = new BlobDownloadResult(stream, "data.json");

        // ReSharper disable once NotDisposedResource
        _storage.OpenReadStreamAsync(location, AbortToken).Returns(downloadResult);

        // Act
        var result = await _storage.GetBlobContentAsync(location, TestDataJsonContext.Default.TestData, AbortToken);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("AOT");
        result.Value.Should().Be(123);
    }

    #endregion
}

/// <summary>Minimal in-memory <see cref="IBlobStorage"/> used to prove content/JSON helpers round-trip end to end.</summary>
file sealed class InMemoryBlobStorage : IBlobStorage
{
    private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

    // The dictionary materializes lazily; uploads never require a provisioned container.
    public bool RequiresContainerProvisioning => false;

    public async ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _store[location.ToString()] = buffer.ToArray();
    }

    public ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        if (!_store.TryGetValue(location.ToString(), out var bytes))
        {
            return ValueTask.FromResult<BlobDownloadResult?>(null);
        }

#pragma warning disable CA2000 // Dispose objects before losing scope
        var stream = new MemoryStream(bytes);
        var result = new BlobDownloadResult(stream, location.Path);
#pragma warning restore CA2000

        return ValueTask.FromResult<BlobDownloadResult?>(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<BlobInfo?> GetBlobInfoAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

public sealed class TestData
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

[JsonSerializable(typeof(TestData))]
internal sealed partial class TestDataJsonContext : JsonSerializerContext;
