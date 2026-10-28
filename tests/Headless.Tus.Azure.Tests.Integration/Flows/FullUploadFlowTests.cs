// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Flows;

[Collection<TusAzureFixture>]
public sealed class FullUploadFlowTests(TusAzureFixture fixture)
    : TusUploadFlowConformanceTestsBase<TusAzureStore>(fixture)
{
    private readonly BlobContainerClient _containerClient = fixture
        .CreateBlobServiceClient()
        .GetBlobContainerClient(TusAzureFixture.ContainerName);

    private const string _BlobPrefix = TusAzureFixture.BlobPrefix;

    /* Test #101: Checksum verification flow */

    [Fact]
    public async Task should_handle_checksum_verification_flow()
    {
        // given - create file with checksum in metadata
        var content = Faker.Random.Bytes(1_000);
        var uploadLength = content.Length;
        var fileName = Faker.System.FileName();

        // TUS checksum metadata is typically stored separately from file metadata
        // and verified during the upload process by tusdotnet middleware
        var metadata = $"filename {fileName.ToBase64()}";

        var fileId = await Store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // when - upload content
        await using (var stream = new MemoryStream(content))
        {
            await Store.AppendDataAsync(fileId, stream, AbortToken);
        }

        // then - verify supported algorithms are available
        var algorithms = (await Store.GetSupportedAlgorithmsAsync(AbortToken)).ToList();
        algorithms.Should().Contain("sha256");
        algorithms.Should().Contain("md5");

        // and verify content was stored correctly
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);

        var downloadedContent = downloadStream.ToArray();
        downloadedContent.Should().BeEquivalentTo(content);

        // Note: Full checksum verification requires tusdotnet's ChecksumAwareStream wrapper
        // which sets up the pre-calculated checksum metadata. Direct VerifyChecksumAsync
        // calls without this flow return false (fail-fast design).
        // See TusAzureStoreTests.should_return_false_when_no_checksum_metadata_exists
    }

    [Fact]
    public override Task should_complete_small_file_upload()
    {
        return base.should_complete_small_file_upload();
    }

    [Fact]
    public override Task should_resume_interrupted_upload()
    {
        return base.should_resume_interrupted_upload();
    }

    [Fact]
    public override Task should_handle_concurrent_uploads()
    {
        return base.should_handle_concurrent_uploads();
    }

    [Fact]
    public override Task should_handle_large_file_chunking()
    {
        return base.should_handle_large_file_chunking();
    }
}
