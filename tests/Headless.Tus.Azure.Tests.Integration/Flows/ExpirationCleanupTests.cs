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
        // given - create expired INCOMPLETE uploads (the TUS Expiration extension targets
        // unfinished uploads only)
        var expiredFileId1 = await _CreateIncompleteUploadAsync(1_000, uploadedBytes: 500);
        await _store.SetExpirationAsync(expiredFileId1, DateTimeOffset.UtcNow.AddMinutes(-30), AbortToken);

        var expiredFileId2 = await _CreateIncompleteUploadAsync(1_400, uploadedBytes: 700);
        await _store.SetExpirationAsync(expiredFileId2, DateTimeOffset.UtcNow.AddMinutes(-15), AbortToken);

        // given - create active incomplete file (not expired)
        var activeFileId = await _CreateIncompleteUploadAsync(1_200, uploadedBytes: 600);
        await _store.SetExpirationAsync(activeFileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);

        // verify all files exist before cleanup
        (await _store.FileExistAsync(expiredFileId1, AbortToken))
            .Should()
            .BeTrue();
        (await _store.FileExistAsync(expiredFileId2, AbortToken)).Should().BeTrue();
        (await _store.FileExistAsync(activeFileId, AbortToken)).Should().BeTrue();

        // when - run expiration cleanup
        var removedCount = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - expired incomplete files should be removed
        removedCount.Should().BeGreaterThanOrEqualTo(2);

        (await _store.FileExistAsync(expiredFileId1, AbortToken)).Should().BeFalse();
        (await _store.FileExistAsync(expiredFileId2, AbortToken)).Should().BeFalse();

        // and active file should still exist
        (await _store.FileExistAsync(activeFileId, AbortToken))
            .Should()
            .BeTrue();
    }

    /* Test: Completed uploads survive expiration cleanup */

    [Fact]
    public async Task should_not_delete_expired_completed_uploads()
    {
        // given - a COMPLETED upload whose expiration has passed. tusdotnet refreshes the sliding
        // expiration on the completing PATCH, so completed uploads routinely carry a past
        // tus_expiration; cleanup must never destroy them (TusDiskStore parity).
        var content = Faker.Random.Bytes(800);
        var completedFileId = await _CreateAndUploadFileAsync(content);
        await _store.SetExpirationAsync(completedFileId, DateTimeOffset.UtcNow.AddMinutes(-30), AbortToken);

        // when
        var expiredFiles = (await _store.GetExpiredFilesAsync(AbortToken)).ToList();
        _ = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - the completed upload is neither listed as expired nor deleted
        expiredFiles.Should().NotContain(completedFileId);
        (await _store.FileExistAsync(completedFileId, AbortToken)).Should().BeTrue();

        // and its content is intact
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + completedFileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);
        downloadStream.ToArray().Should().BeEquivalentTo(content);
    }

    /* Test: Defer-length uploads (no declared length) count as incomplete */

    [Fact]
    public async Task should_cleanup_expired_defer_length_uploads()
    {
        // given - an expired defer-length upload that never declared its final length; an upload
        // with unknown length is unfinished by definition (TusDiskStore parity)
        var fileId = await _store.CreateFileAsync(-1L, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(Faker.Random.Bytes(300)))
        {
            await _store.AppendDataAsync(fileId, stream, AbortToken);
        }

        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddMinutes(-5), AbortToken);

        // when
        var removedCount = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then
        removedCount.Should().BeGreaterThanOrEqualTo(1);
        (await _store.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
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

        // when - run expiration cleanup (return value intentionally ignored; the assertions below
        // verify our active files are preserved, not the removed count)
        _ = await _store.RemoveExpiredFilesAsync(AbortToken);

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

    /* Test: Completed uploads report no expiration (tus HEAD/DELETE must keep working) */

    [Fact]
    public async Task should_report_no_expiration_for_completed_uploads()
    {
        // given - a completed upload whose sliding expiration has passed (tusdotnet refreshes the
        // expiration on the completing PATCH, so this state is routine)
        var content = Faker.Random.Bytes(400);
        var fileId = await _CreateAndUploadFileAsync(content);
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddMinutes(-10), AbortToken);

        // when
        var expiration = await _store.GetExpirationAsync(fileId, AbortToken);

        // then - null keeps tusdotnet's FileHasNotExpired requirement from 404ing the completed
        // upload, so HEAD and DELETE (termination) keep working after the window elapses
        expiration.Should().BeNull();

        // and an incomplete upload still reports its expiration
        var incompleteId = await _CreateIncompleteUploadAsync(1_000, uploadedBytes: 300);
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        await _store.SetExpirationAsync(incompleteId, expires, AbortToken);
        (await _store.GetExpirationAsync(incompleteId, AbortToken))
            .Should()
            .BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }

    /* Test: SetExpiration must complete even with a cancelled request token */

    [Fact]
    public async Task should_set_expiration_even_when_request_token_is_cancelled()
    {
        // given - tusdotnet refreshes the sliding expiration AFTER a PATCH with the request's
        // token, which is already cancelled when the client paused/disconnected mid-request —
        // exactly when the committed partial data still needs its window extended to resume later
        var fileId = await _CreateIncompleteUploadAsync(1_000, uploadedBytes: 500);
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // when
        await _store.SetExpirationAsync(fileId, expires, cancelled.Token);

        // then
        var stored = await _store.GetExpirationAsync(fileId, AbortToken);
        stored.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }

    /* Test: GetExpiredFilesAsync returns correct files */

    [Fact]
    public async Task should_list_only_expired_files()
    {
        // given - create mix of expired and active INCOMPLETE files
        var expiredFileId = await _CreateIncompleteUploadAsync(800, uploadedBytes: 400);
        await _store.SetExpirationAsync(expiredFileId, DateTimeOffset.UtcNow.AddMinutes(-5), AbortToken);

        var activeFileId = await _CreateIncompleteUploadAsync(1_000, uploadedBytes: 500);
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

    private async Task<string> _CreateIncompleteUploadAsync(int declaredLength, int uploadedBytes)
    {
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(declaredLength, metadata, AbortToken);

        await using var stream = new MemoryStream(Faker.Random.Bytes(uploadedBytes));
        await _store.AppendDataAsync(fileId, stream, AbortToken);

        return fileId;
    }
}
