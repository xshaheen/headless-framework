// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Tests.TestSetup;

namespace Tests.Flows;

[Collection<TusAzureFixture>]
public sealed class ExpirationCleanupTests : TestBase
{
    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private const string _ContainerName = "tuscontainer";
    private const string _BlobPrefix = "tusfiles/";

    public ExpirationCleanupTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions { ContainerName = _ContainerName, BlobPrefix = _BlobPrefix };
        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    /* Test #105: Full expiration cleanup flow */

    [Fact]
    public async Task should_cleanup_expired_uploads()
    {
        // given - create expired files
        var expiredFileData1 = Faker.Random.Bytes(500);
        var expiredFileId1 = await _CreateAndUploadFileAsync(expiredFileData1);
        await _store.SetExpirationAsync(expiredFileId1, DateTimeOffset.UtcNow.AddMinutes(-30), AbortToken);

        var expiredFileData2 = Faker.Random.Bytes(700);
        var expiredFileId2 = await _CreateAndUploadFileAsync(expiredFileData2);
        await _store.SetExpirationAsync(expiredFileId2, DateTimeOffset.UtcNow.AddMinutes(-15), AbortToken);

        // given - create active file (not expired)
        var activeFileData = Faker.Random.Bytes(600);
        var activeFileId = await _CreateAndUploadFileAsync(activeFileData);
        await _store.SetExpirationAsync(activeFileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);

        // verify all files exist before cleanup
        (await _store.FileExistAsync(expiredFileId1, AbortToken))
            .Should()
            .BeTrue();
        (await _store.FileExistAsync(expiredFileId2, AbortToken)).Should().BeTrue();
        (await _store.FileExistAsync(activeFileId, AbortToken)).Should().BeTrue();

        // when - run expiration cleanup
        var removedCount = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - expired files should be removed
        removedCount.Should().BeGreaterThanOrEqualTo(2);

        (await _store.FileExistAsync(expiredFileId1, AbortToken)).Should().BeFalse();
        (await _store.FileExistAsync(expiredFileId2, AbortToken)).Should().BeFalse();

        // and active file should still exist
        (await _store.FileExistAsync(activeFileId, AbortToken))
            .Should()
            .BeTrue();

        // and active file content should be preserved
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + activeFileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);
        downloadStream.ToArray().Should().BeEquivalentTo(activeFileData);
    }

    /* Test #106: Active uploads not deleted */

    [Fact]
    public async Task should_not_delete_active_uploads()
    {
        // given - create multiple active (non-expired) files
        var activeFiles = new List<(string FileId, byte[] Content)>();

        for (var i = 0; i < 3; i++)
        {
            var content = Faker.Random.Bytes(Faker.Random.Number(400, 800));
            var fileId = await _CreateAndUploadFileAsync(content);

            // Set expiration in the future
            await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddHours(i + 1), AbortToken);

            activeFiles.Add((fileId, content));
        }

        // given - also create a file without expiration (should be treated as active)
        var noExpirationContent = Faker.Random.Bytes(500);
        var noExpirationFileId = await _CreateAndUploadFileAsync(noExpirationContent);

        // verify all files exist before cleanup
        foreach (var (fileId, _) in activeFiles)
        {
            (await _store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
        }
        (await _store.FileExistAsync(noExpirationFileId, AbortToken)).Should().BeTrue();

        // when - run expiration cleanup
        var removedCount = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - no active files should be removed (may be 0 or more if other expired files exist)
        // The key assertion is that OUR active files are preserved
        foreach (var (fileId, content) in activeFiles)
        {
            // File should still exist
            (await _store.FileExistAsync(fileId, AbortToken))
                .Should()
                .BeTrue();

            // Content should be preserved
            var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
            await using var downloadStream = new MemoryStream();
            await blobClient.DownloadToAsync(downloadStream, AbortToken);
            downloadStream.ToArray().Should().BeEquivalentTo(content);
        }

        // and file without expiration should still exist
        (await _store.FileExistAsync(noExpirationFileId, AbortToken))
            .Should()
            .BeTrue();

        var noExpBlobClient = _containerClient.GetBlobClient(_BlobPrefix + noExpirationFileId);
        await using var noExpDownloadStream = new MemoryStream();
        await noExpBlobClient.DownloadToAsync(noExpDownloadStream, AbortToken);
        noExpDownloadStream.ToArray().Should().BeEquivalentTo(noExpirationContent);
    }

    /* Test: Expired incomplete uploads should be cleaned */

    [Fact]
    public async Task should_cleanup_expired_incomplete_uploads()
    {
        // given - create incomplete upload (partially uploaded)
        var fullContent = Faker.Random.Bytes(1_000);
        var partialContent = fullContent[..500]; // Only first half

        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(fullContent.Length, metadata, AbortToken);

        // Upload only partial content
        await using (var stream = new MemoryStream(partialContent))
        {
            await _store.AppendDataAsync(fileId, stream, AbortToken);
        }

        // Set as expired
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddMinutes(-10), AbortToken);

        // Verify file exists and is incomplete
        (await _store.FileExistAsync(fileId, AbortToken))
            .Should()
            .BeTrue();
        var offset = await _store.GetUploadOffsetAsync(fileId, AbortToken);
        var length = await _store.GetUploadLengthAsync(fileId, AbortToken);
        length.Should().NotBeNull();
        offset.Should().BeLessThan(length!.Value);

        // when - run cleanup
        var removedCount = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - expired incomplete upload should be removed
        removedCount.Should().BeGreaterThanOrEqualTo(1);
        (await _store.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
    }

    /* Test: GetExpiredFilesAsync returns correct files */

    [Fact]
    public async Task should_list_only_expired_files()
    {
        // given - create mix of expired and active files
        var expiredContent = Faker.Random.Bytes(400);
        var expiredFileId = await _CreateAndUploadFileAsync(expiredContent);
        await _store.SetExpirationAsync(expiredFileId, DateTimeOffset.UtcNow.AddMinutes(-5), AbortToken);

        var activeContent = Faker.Random.Bytes(500);
        var activeFileId = await _CreateAndUploadFileAsync(activeContent);
        await _store.SetExpirationAsync(activeFileId, DateTimeOffset.UtcNow.AddHours(2), AbortToken);

        // when - get expired files list
        var expiredFiles = (await _store.GetExpiredFilesAsync(AbortToken)).ToList();

        // then - only expired file should be in the list
        expiredFiles.Should().Contain(expiredFileId);
        expiredFiles.Should().NotContain(activeFileId);
    }

    private async Task<string> _CreateAndUploadFileAsync(byte[] content)
    {
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(content.Length, metadata, AbortToken);

        await using var stream = new MemoryStream(content);
        await _store.AppendDataAsync(fileId, stream, AbortToken);

        return fileId;
    }
}
