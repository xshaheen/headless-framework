// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Headless.Tus;
using Tests.TestSetup;
using TusAzureMetadata = Headless.Tus.Models.TusAzureMetadata;

namespace Tests;

/// <summary>
/// Runs the shared store conformance scenarios against <see cref="TusAzureStore" />, plus the cases that read the
/// Azure blob directly (metadata entries, block lists) or use the Azure-only checksum store.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class TusAzureStoreTests(TusAzureFixture fixture) : TusStoreConformanceTestsBase<TusAzureStore>(fixture)
{
    private readonly BlobContainerClient _containerClient = fixture
        .CreateBlobServiceClient()
        .GetBlobContainerClient(TusAzureFixture.ContainerName);

    private const string _BlobPrefix = TusAzureFixture.BlobPrefix;

    /* Creation Store */

    // -- CreateFile

    [Fact]
    public async Task should_create_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        // when
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        // then
        fileId.Should().NotBeNullOrEmpty();
        // Blob should exist
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var exists = await blobClient.ExistsAsync(AbortToken);
        exists.Value.Should().BeTrue();
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        // The Upload-Metadata header value is stored verbatim in a single metadata entry
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.RawMetadataKey);
        properties.Value.Metadata[TusAzureMetadata.RawMetadataKey].Should().Be(metadata);
        // Created date should be set correctly
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.CreatedDateKey);
        // Upload length should be set correctly
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(uploadLength.ToInvariantString());
    }

    /* Creation Defer Length Store */

    // -- SetUploadLength

    [Fact]
    public async Task should_set_upload_length()
    {
        // given - tusdotnet passes -1 for Upload-Defer-Length creations
        const long initialUploadLength = -1L;
        var newUploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(initialUploadLength, metadata, CancellationToken.None);

        // the -1 sentinel must not be persisted: tusdotnet decides between "Upload-Length" and
        // "Upload-Defer-Length: 1" in HEAD responses based on GetUploadLengthAsync being null
        (await Store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .BeNull();

        // when
        await Store.SetUploadLengthAsync(fileId, newUploadLength, CancellationToken.None);

        // then
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(newUploadLength.ToInvariantString());
    }

    /* Store Core */

    // -- GetUploadOffset

    [Fact]
    public async Task should_get_upload_offset()
    {
        // given
        var uploadLength = Faker.Random.Number(1_000, 5_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var blockBlobClient = _containerClient.GetBlockBlobClient(_BlobPrefix + fileId);

        // Upload some blocks
        List<long> blockSizes = [500, 800, 200];
        List<string> blockIds = [];

        foreach (var size in blockSizes)
        {
            var blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            await using var stream = new MemoryStream(new byte[size]);
            await blockBlobClient.StageBlockAsync(blockId, stream, cancellationToken: AbortToken);
            blockIds.Add(blockId);
        }

        await blockBlobClient.CommitBlockListAsync(blockIds, cancellationToken: CancellationToken.None);

        var expectedOffset = blockSizes.Sum();

        // when
        var uploadOffset = await Store.GetUploadOffsetAsync(fileId, CancellationToken.None);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var nonExistentOffset = await Store.GetUploadOffsetAsync(nonExistentFileId, CancellationToken.None);

        // then
        uploadOffset.Should().Be(expectedOffset);
        nonExistentOffset.Should().Be(0);
    }

    /* Termination Store */

    // -- DeleteFile

    [Fact]
    public async Task should_delete_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var existsBeforeDeletion = await blobClient.ExistsAsync(AbortToken);
        existsBeforeDeletion.Value.Should().BeTrue();

        // when
        await Store.DeleteFileAsync(fileId, CancellationToken.None);
        var existsAfterDeletion = await blobClient.ExistsAsync(AbortToken);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        // Deleting a non-existent file should not throw
        var act = async () => await Store.DeleteFileAsync(nonExistentFileId, CancellationToken.None);

        // then
        existsAfterDeletion.Value.Should().BeFalse();
        await act.Should().NotThrowAsync();
    }

    /* Checksum Store */

    // -- GetSupportedAlgorithms

    [Fact]
    public async Task should_return_supported_checksum_algorithms()
    {
        // when
        var algorithms = (await Store.GetSupportedAlgorithmsAsync(CancellationToken.None)).ToList();

        // then
        algorithms.Should().Contain("sha1");
        algorithms.Should().Contain("sha256");
        algorithms.Should().Contain("sha512");
        algorithms.Should().Contain("md5");
    }

    // -- VerifyChecksum
    // Note: Full checksum two-phase commit flow requires tusdotnet's internal ChecksumAwareStream wrapper.
    // These tests verify API behavior; the header flow is covered in ChecksumRoundTripTests and the
    // trailer flow (verify-after-commit) in ChecksumTrailerTests.

    [Fact]
    public async Task should_verify_committed_data_when_checksum_arrives_after_append()
    {
        // given - an append without checksum info, as a checksum-trailer PATCH looks to the store
        var dataToAppend = Faker.Random.Bytes(3_000);
        var fileId = await Store.CreateFileAsync(dataToAppend.Length, metadata: null, CancellationToken.None);

        await using var stream = new MemoryStream(dataToAppend);
        await Store.AppendDataAsync(fileId, stream, CancellationToken.None);

        // when - the digest arrives afterwards (trailer flow) and matches
        var checksum = SHA256.HashData(dataToAppend);
        var isValid = await Store.VerifyChecksumAsync(fileId, "sha256", checksum, CancellationToken.None);

        // then - the store hashes the committed chunk on demand and confirms it
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_no_chunk_was_appended()
    {
        // given - a freshly created upload with no appended data
        var fileId = await Store.CreateFileAsync(1_000, metadata: null, CancellationToken.None);

        // when - verify is called without any prior append (nothing to verify)
        var anyChecksum = SHA256.HashData(Faker.Random.Bytes(100));
        var isValid = await Store.VerifyChecksumAsync(fileId, "sha256", anyChecksum, CancellationToken.None);

        // then
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_for_nonexistent_file()
    {
        // given
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var checksum = SHA256.HashData([1, 2, 3]);

        // when
        var isValid = await Store.VerifyChecksumAsync(nonExistentFileId, "sha256", checksum, CancellationToken.None);

        // then - returns false gracefully (no exception)
        isValid.Should().BeFalse();
    }

    /* Expiration Store */

    // -- SetExpiration

    [Fact]
    public async Task should_set_expiration()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var expiration = TimeSpan.FromHours(1);
        var expectedExpirationTime = DateTimeOffset.UtcNow.Add(expiration);
        // when
        await Store.SetExpirationAsync(fileId, expectedExpirationTime, CancellationToken.None);
        // then
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.ExpirationKey);
        var storedExpiration = DateTimeOffset.Parse(
            properties.Value.Metadata[TusAzureMetadata.ExpirationKey],
            CultureInfo.InvariantCulture
        );
        storedExpiration.Should().BeCloseTo(expectedExpirationTime, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public override Task should_get_upload_metadata()
    {
        return base.should_get_upload_metadata();
    }

    [Fact]
    public override Task should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys()
    {
        return base.should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys();
    }

    [Fact]
    public override Task should_upload_data_before_upload_length_is_known()
    {
        return base.should_upload_data_before_upload_length_is_known();
    }

    [Fact]
    public override Task should_check_file_existence()
    {
        return base.should_check_file_existence();
    }

    [Fact]
    public override Task should_get_upload_length()
    {
        return base.should_get_upload_length();
    }

    [Fact]
    public override Task should_get_file()
    {
        return base.should_get_file();
    }

    [Fact]
    public override Task should_append_data_when_file_exists()
    {
        return base.should_append_data_when_file_exists();
    }

    [Fact]
    public override Task should_throw_when_append_data_to_nonexistent_file()
    {
        return base.should_throw_when_append_data_to_nonexistent_file();
    }

    [Fact]
    public override Task should_append_data_using_pipe_when_file_exists()
    {
        return base.should_append_data_using_pipe_when_file_exists();
    }

    [Fact]
    public override Task should_throw_when_append_data_using_pipe_to_nonexistent_file()
    {
        return base.should_throw_when_append_data_using_pipe_to_nonexistent_file();
    }

    [Fact]
    public override Task should_persist_received_bytes_when_client_disconnects_mid_stream()
    {
        return base.should_persist_received_bytes_when_client_disconnects_mid_stream();
    }

    [Fact]
    public override Task should_persist_received_bytes_when_pipe_read_is_cancelled()
    {
        return base.should_persist_received_bytes_when_pipe_read_is_cancelled();
    }

    [Fact]
    public override Task should_get_expiration()
    {
        return base.should_get_expiration();
    }

    [Fact]
    public override Task should_get_expired_files()
    {
        return base.should_get_expired_files();
    }
}
