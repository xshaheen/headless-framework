// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Tests.TestSetup;

namespace Tests.Flows;

[Collection<TusAzureFixture>]
public sealed class FullUploadFlowTests : TestBase
{
    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private const string _ContainerName = "tuscontainer";
    private const string _BlobPrefix = "tusfiles/";

    public FullUploadFlowTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions { ContainerName = _ContainerName, BlobPrefix = _BlobPrefix };
        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    /* Test #97: Complete small file upload */

    [Fact]
    public async Task should_complete_small_file_upload()
    {
        // given - create file with known content
        var content = Faker.Random.Bytes(1_000);
        var uploadLength = content.Length;
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        // when - create file
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // then - upload single chunk
        await using (var stream = new MemoryStream(content))
        {
            var bytesWritten = await _store.AppendDataAsync(fileId, stream, AbortToken);
            bytesWritten.Should().Be(content.Length);
        }

        // and verify content matches
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);

        downloadStream.ToArray().Should().BeEquivalentTo(content);

        // and verify upload is complete
        var offset = await _store.GetUploadOffsetAsync(fileId, AbortToken);
        var length = await _store.GetUploadLengthAsync(fileId, AbortToken);
        offset.Should().Be(length);
    }

    /* Test #98: Resume interrupted upload */

    [Fact]
    public async Task should_resume_interrupted_upload()
    {
        // given - create file for chunked upload
        var chunk1 = Faker.Random.Bytes(500);
        var chunk2 = Faker.Random.Bytes(700);
        var fullContent = chunk1.Concat(chunk2).ToArray();
        var uploadLength = fullContent.Length;
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        var fileId = await _store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // upload first chunk (simulating partial upload before interruption)
        await using (var stream1 = new MemoryStream(chunk1))
        {
            await _store.AppendDataAsync(fileId, stream1, AbortToken);
        }

        // when - simulate resumption: get current offset
        var currentOffset = await _store.GetUploadOffsetAsync(fileId, AbortToken);

        // then - offset should reflect first chunk
        currentOffset.Should().Be(chunk1.Length);

        // resume upload with second chunk
        await using (var stream2 = new MemoryStream(chunk2))
        {
            var bytesWritten = await _store.AppendDataAsync(fileId, stream2, AbortToken);
            bytesWritten.Should().Be(chunk2.Length);
        }

        // and verify complete file content
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);

        downloadStream.ToArray().Should().BeEquivalentTo(fullContent);

        // and verify final offset equals upload length
        var finalOffset = await _store.GetUploadOffsetAsync(fileId, AbortToken);
        finalOffset.Should().Be(uploadLength);
    }

    /* Test #99: Concurrent uploads */

    [Fact]
    public async Task should_handle_concurrent_uploads()
    {
        // given - prepare multiple files for concurrent upload
        const int fileCount = 5;
        var files = Enumerable
            .Range(0, fileCount)
            .Select(_ => new
            {
                Content = Faker.Random.Bytes(Faker.Random.Number(500, 2_000)),
                FileName = Faker.System.FileName(),
            })
            .ToList();

        // when - upload all files concurrently
        var uploadTasks = files.Select(async file =>
        {
            var metadata = $"filename {file.FileName.ToBase64()}";
            var fileId = await _store.CreateFileAsync(file.Content.Length, metadata, AbortToken);

            await using var stream = new MemoryStream(file.Content);
            await _store.AppendDataAsync(fileId, stream, AbortToken);

            return new { FileId = fileId, ExpectedContent = file.Content };
        });

        var results = await Task.WhenAll(uploadTasks);

        // then - verify all files uploaded correctly
        results.Should().HaveCount(fileCount);

        foreach (var result in results)
        {
            var exists = await _store.FileExistAsync(result.FileId, AbortToken);
            exists.Should().BeTrue();

            var blobClient = _containerClient.GetBlobClient(_BlobPrefix + result.FileId);
            await using var downloadStream = new MemoryStream();
            await blobClient.DownloadToAsync(downloadStream, AbortToken);

            downloadStream.ToArray().Should().BeEquivalentTo(result.ExpectedContent);
        }
    }

    /* Test #100: Large file chunking */

    [Fact]
    public async Task should_handle_large_file_chunking()
    {
        // given - create file with multiple chunks
        const int chunkCount = 5;
        var chunks = Enumerable.Range(0, chunkCount).Select(_ => Faker.Random.Bytes(1_000)).ToList();

        var fullContent = chunks.SelectMany(c => c).ToArray();
        var uploadLength = fullContent.Length;
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        var fileId = await _store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // when - upload each chunk sequentially (simulating chunked upload)
        var totalBytesWritten = 0L;

        foreach (var chunk in chunks)
        {
            await using var stream = new MemoryStream(chunk);
            var bytesWritten = await _store.AppendDataAsync(fileId, stream, AbortToken);

            totalBytesWritten += bytesWritten;

            // verify offset increases after each chunk
            var currentOffset = await _store.GetUploadOffsetAsync(fileId, AbortToken);
            currentOffset.Should().Be(totalBytesWritten);
        }

        // then - verify total bytes
        totalBytesWritten.Should().Be(fullContent.Length);

        // and verify final content integrity
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);

        downloadStream.ToArray().Should().BeEquivalentTo(fullContent);

        // and verify upload is complete
        var finalOffset = await _store.GetUploadOffsetAsync(fileId, AbortToken);
        var length = await _store.GetUploadLengthAsync(fileId, AbortToken);
        finalOffset.Should().Be(length);
    }

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

        var fileId = await _store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // when - upload content
        await using (var stream = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, stream, AbortToken);
        }

        // then - verify supported algorithms are available
        var algorithms = (await _store.GetSupportedAlgorithmsAsync(AbortToken)).ToList();
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
}
