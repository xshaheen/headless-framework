// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Blobs;
using Headless.Primitives;
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
            Size = 0,
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
            Size = 0,
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
            Size = 0,
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
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["contentType"] = "text/plain" };

        // Act
        var blobInfo = new BlobInfo
        {
            BlobKey = "file.txt",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Size = 0,
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
    public void should_store_path_when_creating_upload_request()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var request = new BlobUploadRequest("document.pdf", stream);

        // Assert
        request.Path.Should().Be("document.pdf");
    }

    [Fact]
    public void should_store_stream_when_creating_upload_request()
    {
        // Arrange
        var stream = new MemoryStream([1, 2, 3]);

        // Act
        var request = new BlobUploadRequest("file.bin", stream);

        // Assert
        request.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public void should_store_metadata_when_creating_upload_request_with_metadata()
    {
        // Arrange
        var stream = new MemoryStream();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["author"] = "test" };

        // Act
        var request = new BlobUploadRequest("file.txt", stream, metadata);

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
        var request = new BlobUploadRequest("file.txt", stream);

        // Assert
        request.Metadata.Should().BeNull();
    }

    #endregion

    #region BlobQuery Tests

    [Fact]
    public void should_round_trip_properties_when_query_constructed_with_valid_values()
    {
        // Act
        var query = new BlobQuery("bucket", "prefix/sub", pageSize: 25, continuationToken: "token-1");

        // Assert
        query.Container.Should().Be("bucket");
        query.Prefix.Should().Be("prefix/sub");
        query.PageSize.Should().Be(25);
        query.ContinuationToken.Should().Be("token-1");
    }

    [Fact]
    public void should_default_optional_values_when_only_container_provided()
    {
        // Act
        var query = new BlobQuery("bucket");

        // Assert
        query.Prefix.Should().BeNull();
        query.PageSize.Should().Be(100);
        query.ContinuationToken.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_query_container_is_null_or_blank(string? container)
    {
        // Act
        var act = () => new BlobQuery(container!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_query_prefix_contains_traversal()
    {
        // Act
        var act = () => new BlobQuery("bucket", "../escape");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_query_prefix_is_sidecar_suffix()
    {
        // Act
        var act = () => new BlobQuery("bucket", "report.hlmeta");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void should_throw_when_query_page_size_is_not_positive(int pageSize)
    {
        // Act
        var act = () => new BlobQuery("bucket", pageSize: pageSize);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region BlobPage Tests

    [Fact]
    public void should_have_no_items_and_null_token_when_using_empty_page()
    {
        // Act
        var page = BlobPage.Empty;

        // Assert
        page.Items.Should().BeEmpty();
        page.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public void should_carry_items_and_token_when_page_constructed()
    {
        // Arrange
        var items = new List<BlobInfo>
        {
            new()
            {
                BlobKey = "file1.txt",
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
                Size = 0,
            },
        };

        // Act
        var page = new BlobPage(items, "next-token");

        // Assert
        page.Items.Should().HaveCount(1);
        page.Items[0].BlobKey.Should().Be("file1.txt");
        page.ContinuationToken.Should().Be("next-token");
    }

    #endregion

    #region BlobBulkResult Tests

    [Fact]
    public void should_carry_location_identity_and_success_result_when_constructed()
    {
        // Arrange
        var location = new BlobLocation("bucket", "file.txt");

        // Act
        var result = new BlobBulkResult(location, Result<bool, Exception>.Ok(true));

        // Assert
        result.Container.Should().Be("bucket");
        result.Path.Should().Be("file.txt");
        result.Location.Should().Be(location);
        result.Result.IsSuccess.Should().BeTrue();
        result.Result.Value.Should().BeTrue();
    }

    [Fact]
    public void should_carry_location_identity_and_failure_result_when_constructed_with_error()
    {
        // Arrange
        var location = new BlobLocation("bucket", "file.txt");
        var error = new InvalidOperationException("boom");

        // Act
        var result = new BlobBulkResult(location, Result<bool, Exception>.Fail(error));

        // Assert
        result.Container.Should().Be("bucket");
        result.Path.Should().Be("file.txt");
        result.Location.Should().Be(location);
        result.Result.IsFailure.Should().BeTrue();
        result.Result.Error.Should().BeSameAs(error);
    }

    [Fact]
    public void should_carry_raw_identity_without_location_when_input_path_is_invalid()
    {
        // Arrange
        var error = new ArgumentException("Invalid path.");

        // Act
        var result = new BlobBulkResult("bucket", "../escape.txt", Result<bool, Exception>.Fail(error));

        // Assert
        result.Container.Should().Be("bucket");
        result.Path.Should().Be("../escape.txt");
        result.Location.Should().BeNull();
        result.Result.IsFailure.Should().BeTrue();
        result.Result.Error.Should().BeSameAs(error);
    }

    #endregion
}
