// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus.Locks;
using Headless.Tus.Options;
using Microsoft.Extensions.Logging;
using Tests.TestSetup;

namespace Tests.Locks;

[Collection<TusAzureFixture>]
public sealed class AzureBlobFileLockTests : TestBase
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly TusAzureStoreOptions _options;
    private const string _ContainerName = "lockstestcontainer";
    private const string _BlobPrefix = "lockfiles/";

    public AzureBlobFileLockTests(TusAzureFixture fixture)
    {
        _blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        _options = new TusAzureStoreOptions
        {
            ContainerName = _ContainerName,
            BlobPrefix = _BlobPrefix,
            LeaseDuration = TimeSpan.FromSeconds(15), // Minimum allowed lease duration
        };

        _containerClient = _blobServiceClient.GetBlobContainerClient(_ContainerName);
        _containerClient.CreateIfNotExists();
    }

    /* AzureBlobFileLock Tests */

    [Fact]
    public async Task should_acquire_blob_lease()
    {
        // given
        var fileId = Guid.NewGuid().ToString();
        var blobClient = await _CreateBlobAsync(fileId);
        var fileLock = new AzureBlobFileLock(
            blobClient,
            _options.LeaseDuration,
            LoggerFactory.CreateLogger<AzureBlobFileLock>()
        );

        // when
        var lockAcquired = await fileLock.Lock();

        // then
        lockAcquired.Should().BeTrue();

        // cleanup
        await fileLock.ReleaseIfHeld();
    }

    [Fact]
    public async Task should_return_false_when_lease_taken()
    {
        // given
        var fileId = Guid.NewGuid().ToString();
        var blobClient = await _CreateBlobAsync(fileId);

        var firstLock = new AzureBlobFileLock(
            blobClient,
            _options.LeaseDuration,
            LoggerFactory.CreateLogger<AzureBlobFileLock>()
        );
        var secondLock = new AzureBlobFileLock(
            blobClient,
            _options.LeaseDuration,
            LoggerFactory.CreateLogger<AzureBlobFileLock>()
        );

        // when
        var firstLockAcquired = await firstLock.Lock();
        var secondLockAcquired = await secondLock.Lock();

        // then
        firstLockAcquired.Should().BeTrue();
        secondLockAcquired.Should().BeFalse();

        // cleanup
        await firstLock.ReleaseIfHeld();
    }

    [Fact]
    public async Task should_release_lease_when_held()
    {
        // given
        var fileId = Guid.NewGuid().ToString();
        var blobClient = await _CreateBlobAsync(fileId);
        var fileLock = new AzureBlobFileLock(
            blobClient,
            _options.LeaseDuration,
            LoggerFactory.CreateLogger<AzureBlobFileLock>()
        );
        await fileLock.Lock();

        // when
        await fileLock.ReleaseIfHeld();

        // then - verify lease is released by acquiring it again
        var newLock = new AzureBlobFileLock(
            blobClient,
            _options.LeaseDuration,
            LoggerFactory.CreateLogger<AzureBlobFileLock>()
        );
        var canAcquire = await newLock.Lock();
        canAcquire.Should().BeTrue();

        // cleanup
        await newLock.ReleaseIfHeld();
    }

    [Fact]
    public async Task should_handle_release_when_no_lease()
    {
        // given
        var fileId = Guid.NewGuid().ToString();
        var blobClient = await _CreateBlobAsync(fileId);
        var fileLock = new AzureBlobFileLock(
            blobClient,
            _options.LeaseDuration,
            LoggerFactory.CreateLogger<AzureBlobFileLock>()
        );

        // when - release without ever acquiring
        var act = () => fileLock.ReleaseIfHeld();

        // then - should not throw
        await act.Should().NotThrowAsync();
    }

    /* AzureBlobFileLockProvider Tests */

    [Fact]
    public async Task should_create_file_lock_for_id()
    {
        // given
        var fileId = Guid.NewGuid().ToString();
        await _CreateBlobAsync(fileId);
        var provider = new AzureBlobFileLockProvider(_blobServiceClient, _options, LoggerFactory);

        // when
        var fileLock = await provider.AquireLock(fileId);

        // then
        fileLock.Should().NotBeNull();
        fileLock.Should().BeOfType<AzureBlobFileLock>();

        // verify the lock actually works
        var lockAcquired = await fileLock.Lock();
        lockAcquired.Should().BeTrue();

        // cleanup
        await fileLock.ReleaseIfHeld();
    }

    [Fact]
    public async Task should_use_correct_blob_client()
    {
        // given - create blob with specific path
        var fileId = Guid.NewGuid().ToString();
        await _CreateBlobAsync(fileId);
        var provider = new AzureBlobFileLockProvider(_blobServiceClient, _options, LoggerFactory);

        // when
        var fileLock = await provider.AquireLock(fileId);
        var lockAcquired = await fileLock.Lock();

        // then
        lockAcquired.Should().BeTrue();

        // verify the blob at expected path is locked
        var expectedBlobPath = $"{_BlobPrefix}{fileId}";
        var blobClient = _containerClient.GetBlobClient(expectedBlobPath);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.LeaseState.Should().Be(Azure.Storage.Blobs.Models.LeaseState.Leased);

        // cleanup
        await fileLock.ReleaseIfHeld();
    }

    private async Task<BlobClient> _CreateBlobAsync(string fileId)
    {
        var blobPath = $"{_BlobPrefix}{fileId}";
        var blobClient = _containerClient.GetBlobClient(blobPath);

        // Create an empty blob (required for leasing)
        await using var stream = new MemoryStream([]);
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: AbortToken);

        return blobClient;
    }
}
