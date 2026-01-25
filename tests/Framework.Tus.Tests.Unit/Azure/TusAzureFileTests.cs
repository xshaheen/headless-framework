// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Framework.Testing.Tests;
using Framework.Tus.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using tusdotnet.Interfaces;

namespace Tests.Azure;

public sealed class TusAzureFileTests : TestBase
{
    #region TusAzureFile - FromBlobProperties Factory

    [Fact]
    public void should_create_from_blob_properties()
    {
        // given
        var fileId = Faker.Random.Guid().ToString();
        var blobName = Faker.System.FileName();
        var contentLength = Faker.Random.Long(1, 10_000_000);
        var etag = new ETag(Faker.Random.Hash());
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["custom_key"] = "custom_value",
            [TusAzureMetadata.UploadLengthKey] = "12345",
        };
        var properties = BlobsModelFactory.BlobProperties(contentLength: contentLength, eTag: etag, metadata: metadata);

        // when
        var file = TusAzureFile.FromBlobProperties(fileId, blobName, properties);

        // then
        file.FileId.Should().Be(fileId);
        file.BlobName.Should().Be(blobName);
        file.CurrentContentLength.Should().Be(contentLength);
        file.ETag.Should().Be(etag.ToString());
        file.Metadata.Should().NotBeNull();
    }

    [Fact]
    public void should_expose_file_id()
    {
        // given
        var fileId = Faker.Random.Guid().ToString();
        var blobName = Faker.System.FileName();
        var properties = BlobsModelFactory.BlobProperties(
            contentLength: 100,
            eTag: new ETag("test"),
            metadata: _EmptyMetadata()
        );

        // when
        var file = TusAzureFile.FromBlobProperties(fileId, blobName, properties);

        // then
        file.FileId.Should().Be(fileId);
    }

    [Fact]
    public void should_expose_blob_name()
    {
        // given
        var fileId = Faker.Random.Guid().ToString();
        var blobName = "uploads/2024/01/document.pdf";
        var properties = BlobsModelFactory.BlobProperties(
            contentLength: 100,
            eTag: new ETag("test"),
            metadata: _EmptyMetadata()
        );

        // when
        var file = TusAzureFile.FromBlobProperties(fileId, blobName, properties);

        // then
        file.BlobName.Should().Be(blobName);
    }

    [Fact]
    public void should_expose_current_content_length_from_properties()
    {
        // given
        var contentLength = 1024L * 1024L * 5; // 5MB
        var properties = BlobsModelFactory.BlobProperties(
            contentLength: contentLength,
            eTag: new ETag("test"),
            metadata: _EmptyMetadata()
        );

        // when
        var file = TusAzureFile.FromBlobProperties("file-id", "blob-name", properties);

        // then
        file.CurrentContentLength.Should().Be(contentLength);
    }

    [Fact]
    public void should_expose_etag_as_string()
    {
        // given
        var etag = new ETag("0x8DC12345ABCDEF");
        var properties = BlobsModelFactory.BlobProperties(contentLength: 100, eTag: etag, metadata: _EmptyMetadata());

        // when
        var file = TusAzureFile.FromBlobProperties("file-id", "blob-name", properties);

        // then
        file.ETag.Should().Be(etag.ToString());
    }

    [Fact]
    public void should_convert_metadata_from_azure_format()
    {
        // given
        var azureMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filename"] = "test.txt",
            [TusAzureMetadata.UploadLengthKey] = "5000",
            [TusAzureMetadata.CreatedDateKey] = DateTimeOffset.UtcNow.ToString("O"),
        };
        var properties = BlobsModelFactory.BlobProperties(
            contentLength: 100,
            eTag: new ETag("test"),
            metadata: azureMetadata
        );

        // when
        var file = TusAzureFile.FromBlobProperties("file-id", "blob-name", properties);

        // then
        file.Metadata.Should().NotBeNull();
        file.Metadata.UploadLength.Should().Be(5000);
    }

    [Fact]
    public void should_handle_empty_metadata()
    {
        // given
        var properties = BlobsModelFactory.BlobProperties(
            contentLength: 100,
            eTag: new ETag("test"),
            metadata: _EmptyMetadata()
        );

        // when
        var file = TusAzureFile.FromBlobProperties("file-id", "blob-name", properties);

        // then
        file.Metadata.Should().NotBeNull();
        file.Metadata.ToAzure().Should().BeEmpty();
    }

    #endregion

    #region TusAzureFileWrapper - ITusFile Implementation

    [Fact]
    public void should_expose_id_from_azure_file()
    {
        // given
        var fileId = Faker.Random.Guid().ToString();
        var wrapper = _CreateWrapper(fileId);

        // when
        var id = wrapper.Id;

        // then
        id.Should().Be(fileId);
    }

    [Fact]
    public async Task should_get_metadata_dictionary()
    {
        // given
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filename"] = "document.pdf",
            ["contenttype"] = "application/pdf",
        };
        var wrapper = _CreateWrapper("file-id", metadata);

        // when
        var result = await wrapper.GetMetadataAsync(AbortToken);

        // then
        result.Should().NotBeNull();
        result.Should().ContainKey("filename");
        result.Should().ContainKey("contenttype");
        result["filename"].GetString(Encoding.UTF8).Should().Be("document.pdf");
        result["contenttype"].GetString(Encoding.UTF8).Should().Be("application/pdf");
    }

    [Fact]
    public async Task should_return_empty_metadata_when_no_user_metadata()
    {
        // given - only system metadata
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.UploadLengthKey] = "12345",
            [TusAzureMetadata.CreatedDateKey] = DateTimeOffset.UtcNow.ToString("O"),
        };
        var wrapper = _CreateWrapper("file-id", metadata);

        // when
        var result = await wrapper.GetMetadataAsync(AbortToken);

        // then
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_get_content_stream()
    {
        // given
        var contentBytes = Faker.Random.Bytes(1024);
        var contentStream = new MemoryStream(contentBytes);
        var (wrapper, blobClient) = _CreateWrapperWithMockedBlobClient("file-id");
        _SetupDownloadStreamingResponse(blobClient, contentStream);

        // when
        var result = await wrapper.GetContentAsync(AbortToken);

        // then
        result.Should().NotBeNull();
        result.Should().BeSameAs(contentStream);
    }

    [Fact]
    public async Task should_pass_cancellation_token_to_blob_client()
    {
        // given
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var (wrapper, blobClient) = _CreateWrapperWithMockedBlobClient("file-id");
        _SetupDownloadStreamingResponse(blobClient, new MemoryStream());

        // when
        _ = await wrapper.GetContentAsync(token);

        // then
        await blobClient.Received(1).DownloadStreamingAsync(cancellationToken: token);
    }

    [Fact]
    public async Task should_throw_when_download_fails()
    {
        // given
        var fileId = Faker.Random.Guid().ToString();
        var expectedException = new RequestFailedException("Blob not found");
        var (wrapper, blobClient) = _CreateWrapperWithMockedBlobClient(fileId);
        blobClient
            .DownloadStreamingAsync(cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(expectedException);

        // when
        var act = async () => await wrapper.GetContentAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<RequestFailedException>().WithMessage("Blob not found");
    }

    [Fact]
    public void should_implement_itus_file_interface()
    {
        // given
        var wrapper = _CreateWrapper("file-id");

        // then
        wrapper.Should().BeAssignableTo<ITusFile>();
    }

    #endregion

    #region Helpers

    private static Dictionary<string, string> _EmptyMetadata() => [];

    private static ITusFile _CreateWrapper(string fileId, IDictionary<string, string>? metadata = null)
    {
        metadata ??= _EmptyMetadata();
        var properties = BlobsModelFactory.BlobProperties(
            contentLength: 100,
            eTag: new ETag("test"),
            metadata: metadata
        );
        var azureFile = TusAzureFile.FromBlobProperties(fileId, "blob-name", properties);

        // Create a real BlobClient with a fake URI (won't be used for actual calls in these tests)
        var blobClient = new BlobClient(new Uri("https://test.blob.core.windows.net/container/blob"));
        var logger = Substitute.For<ILogger>();

        // Use reflection to create TusAzureFileWrapper since it's internal
        var wrapperType = typeof(TusAzureFile).Assembly.GetType("Framework.Tus.Models.TusAzureFileWrapper")!;
        return (ITusFile)Activator.CreateInstance(wrapperType, azureFile, blobClient, logger)!;
    }

    private static (ITusFile wrapper, BlobClient blobClient) _CreateWrapperWithMockedBlobClient(
        string fileId,
        IDictionary<string, string>? metadata = null
    )
    {
        metadata ??= _EmptyMetadata();
        var properties = BlobsModelFactory.BlobProperties(
            contentLength: 100,
            eTag: new ETag("test"),
            metadata: metadata
        );
        var azureFile = TusAzureFile.FromBlobProperties(fileId, "blob-name", properties);

        // BlobClient has virtual methods that can be mocked
        var blobClient = Substitute.For<BlobClient>();
        var logger = Substitute.For<ILogger>();

        // Use reflection to create TusAzureFileWrapper since it's internal
        var wrapperType = typeof(TusAzureFile).Assembly.GetType("Framework.Tus.Models.TusAzureFileWrapper")!;
        var wrapper = (ITusFile)Activator.CreateInstance(wrapperType, azureFile, blobClient, logger)!;

        return (wrapper, blobClient);
    }

    private static void _SetupDownloadStreamingResponse(BlobClient blobClient, Stream contentStream)
    {
        var downloadStreamingResult = BlobsModelFactory.BlobDownloadStreamingResult(content: contentStream);

        var response = Substitute.For<Response<BlobDownloadStreamingResult>>();
        response.Value.Returns(downloadStreamingResult);

        blobClient
            .DownloadStreamingAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }

    #endregion
}
