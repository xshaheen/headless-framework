// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Framework.Testing.Tests;
using Framework.Tus.Models;
using Framework.Tus.Options;
using Framework.Tus.Services;
using Tests.TestSetup;
using tusdotnet.Models.Concatenation;

namespace Tests.Concatenation;

[Collection<TusAzureFixture>]
public sealed class TusConcatenationTests : TestBase
{
    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private const string _ContainerName = "tuscontainer";
    private const string _BlobPrefix = "tusfiles/";

    public TusConcatenationTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions { ContainerName = _ContainerName, BlobPrefix = _BlobPrefix };
        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    /* CreatePartialFileAsync */

    [Fact]
    public async Task should_create_partial_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        // when
        var fileId = await _store.CreatePartialFileAsync(uploadLength, metadata, AbortToken);

        // then
        fileId.Should().NotBeNullOrEmpty();

        // Verify blob exists
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var exists = await blobClient.ExistsAsync(AbortToken);
        exists.Value.Should().BeTrue();

        // Verify partial marker is set
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.ConcatTypeKey);
        properties.Value.Metadata[TusAzureMetadata.ConcatTypeKey].Should().Be("partial");

        // Verify upload length is set
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(uploadLength.ToInvariantString());

        // Verify created date is set
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.CreatedDateKey);
    }

    [Fact]
    public async Task should_create_partial_file_with_null_metadata()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);

        // when
        var fileId = await _store.CreatePartialFileAsync(uploadLength, metadata: null, AbortToken);

        // then
        fileId.Should().NotBeNullOrEmpty();

        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.Metadata[TusAzureMetadata.ConcatTypeKey].Should().Be("partial");
    }

    /* CreateFinalFileAsync */

    [Fact]
    public async Task should_create_final_file_from_partials()
    {
        // given - create and upload data to two partial files
        var data1 = Faker.Random.Bytes(500);
        var data2 = Faker.Random.Bytes(700);

        var partialId1 = await _store.CreatePartialFileAsync(data1.Length, metadata: null, AbortToken);
        await using (var stream1 = new MemoryStream(data1))
        {
            await _store.AppendDataAsync(partialId1, stream1, AbortToken);
        }

        var partialId2 = await _store.CreatePartialFileAsync(data2.Length, metadata: null, AbortToken);
        await using (var stream2 = new MemoryStream(data2))
        {
            await _store.AppendDataAsync(partialId2, stream2, AbortToken);
        }

        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        // when
        var finalFileId = await _store.CreateFinalFileAsync([partialId1, partialId2], metadata, AbortToken);

        // then
        finalFileId.Should().NotBeNullOrEmpty();

        // Verify final file exists
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + finalFileId);
        var exists = await blobClient.ExistsAsync(AbortToken);
        exists.Value.Should().BeTrue();

        // Verify final marker is set
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.ConcatTypeKey);
        properties.Value.Metadata[TusAzureMetadata.ConcatTypeKey].Should().Be("final");

        // Verify partial uploads are stored
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.PartialUploadsKey);
        var storedPartials = properties.Value.Metadata[TusAzureMetadata.PartialUploadsKey].Split(',');
        storedPartials.Should().Contain(partialId1);
        storedPartials.Should().Contain(partialId2);

        // Verify total size
        var expectedTotalSize = data1.Length + data2.Length;
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(expectedTotalSize.ToInvariantString());
    }

    [Fact]
    public async Task should_throw_when_create_final_file_with_empty_partials()
    {
        // given
        string[] emptyPartials = [];

        // when
        var act = async () => await _store.CreateFinalFileAsync(emptyPartials, metadata: null, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_create_final_file_with_nonexistent_partial()
    {
        // given
        var nonExistentPartialId = "nonexistent-partial-id";

        // when
        var act = async () => await _store.CreateFinalFileAsync([nonExistentPartialId], metadata: null, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"Partial file {nonExistentPartialId} does not exist");
    }

    [Fact]
    public async Task should_throw_when_create_final_file_with_regular_file_as_partial()
    {
        // given - create a regular (non-partial) file
        var uploadLength = Faker.Random.Number(100, 500);
        var regularFileId = await _store.CreateFileAsync(uploadLength, metadata: null, AbortToken);

        // when
        var act = async () => await _store.CreateFinalFileAsync([regularFileId], metadata: null, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"File {regularFileId} is not a partial file");
    }

    [Fact]
    public async Task should_throw_when_create_final_file_with_incomplete_partial()
    {
        // given - create partial file but don't upload all data
        var uploadLength = 1000;
        var partialData = Faker.Random.Bytes(500); // Only half the data
        var partialId = await _store.CreatePartialFileAsync(uploadLength, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(partialData))
        {
            await _store.AppendDataAsync(partialId, stream, AbortToken);
        }

        // when
        var act = async () => await _store.CreateFinalFileAsync([partialId], metadata: null, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"Partial file {partialId} is incomplete*");
    }

    /* GetUploadConcatAsync */

    [Fact]
    public async Task should_get_partial_file_info()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var partialId = await _store.CreatePartialFileAsync(uploadLength, metadata: null, AbortToken);

        // when
        var concat = await _store.GetUploadConcatAsync(partialId, AbortToken);

        // then
        concat.Should().NotBeNull();
        concat.Should().BeOfType<FileConcatPartial>();
    }

    [Fact]
    public async Task should_get_final_file_info()
    {
        // given - create complete partial files
        var data1 = Faker.Random.Bytes(300);
        var data2 = Faker.Random.Bytes(400);

        var partialId1 = await _store.CreatePartialFileAsync(data1.Length, metadata: null, AbortToken);
        await using (var stream1 = new MemoryStream(data1))
        {
            await _store.AppendDataAsync(partialId1, stream1, AbortToken);
        }

        var partialId2 = await _store.CreatePartialFileAsync(data2.Length, metadata: null, AbortToken);
        await using (var stream2 = new MemoryStream(data2))
        {
            await _store.AppendDataAsync(partialId2, stream2, AbortToken);
        }

        var finalFileId = await _store.CreateFinalFileAsync([partialId1, partialId2], metadata: null, AbortToken);

        // when
        var concat = await _store.GetUploadConcatAsync(finalFileId, AbortToken);

        // then
        concat.Should().NotBeNull();
        concat.Should().BeOfType<FileConcatFinal>();

        var finalConcat = (FileConcatFinal)concat!;
        finalConcat.Files.Should().HaveCount(2);
        finalConcat.Files.Should().Contain(partialId1);
        finalConcat.Files.Should().Contain(partialId2);
    }

    [Fact]
    public async Task should_return_null_for_regular_file()
    {
        // given - create a regular (non-concatenation) file
        var uploadLength = Faker.Random.Number(100, 500);
        var regularFileId = await _store.CreateFileAsync(uploadLength, metadata: null, AbortToken);

        // when
        var concat = await _store.GetUploadConcatAsync(regularFileId, AbortToken);

        // then
        concat.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_for_nonexistent_file()
    {
        // given
        const string nonExistentFileId = "nonexistent-file-id";

        // when
        var concat = await _store.GetUploadConcatAsync(nonExistentFileId, AbortToken);

        // then
        concat.Should().BeNull();
    }

    /* Data Integrity */

    [Fact]
    public async Task should_preserve_data_integrity_after_concatenation()
    {
        // given - create partial files with known content
        var data1 = Faker.Random.Bytes(1_000);
        var data2 = Faker.Random.Bytes(1_500);
        var data3 = Faker.Random.Bytes(800);

        var partialId1 = await _store.CreatePartialFileAsync(data1.Length, metadata: null, AbortToken);
        await using (var stream1 = new MemoryStream(data1))
        {
            await _store.AppendDataAsync(partialId1, stream1, AbortToken);
        }

        var partialId2 = await _store.CreatePartialFileAsync(data2.Length, metadata: null, AbortToken);
        await using (var stream2 = new MemoryStream(data2))
        {
            await _store.AppendDataAsync(partialId2, stream2, AbortToken);
        }

        var partialId3 = await _store.CreatePartialFileAsync(data3.Length, metadata: null, AbortToken);
        await using (var stream3 = new MemoryStream(data3))
        {
            await _store.AppendDataAsync(partialId3, stream3, AbortToken);
        }

        // when
        var finalFileId = await _store.CreateFinalFileAsync(
            [partialId1, partialId2, partialId3],
            metadata: null,
            AbortToken
        );

        // then - download and verify content
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + finalFileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);

        var downloadedContent = downloadStream.ToArray();
        var expectedContent = data1.Concat(data2).Concat(data3).ToArray();

        downloadedContent.Should().BeEquivalentTo(expectedContent);
    }

    [Fact]
    public async Task should_create_final_file_from_single_partial()
    {
        // given
        var data = Faker.Random.Bytes(500);
        var partialId = await _store.CreatePartialFileAsync(data.Length, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(data))
        {
            await _store.AppendDataAsync(partialId, stream, AbortToken);
        }

        // when
        var finalFileId = await _store.CreateFinalFileAsync([partialId], metadata: null, AbortToken);

        // then
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + finalFileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);

        downloadStream.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task should_preserve_user_metadata_in_final_file()
    {
        // given
        var data = Faker.Random.Bytes(300);
        var partialId = await _store.CreatePartialFileAsync(data.Length, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(data))
        {
            await _store.AppendDataAsync(partialId, stream, AbortToken);
        }

        var fileName = Faker.System.FileName();
        var customValue = Faker.Random.AlphaNumeric(10);
        var metadata = $"filename {fileName.ToBase64()},customkey {customValue.ToBase64()}";

        // when
        var finalFileId = await _store.CreateFinalFileAsync([partialId], metadata, AbortToken);

        // then
        var retrievedMetadata = await _store.GetUploadMetadataAsync(finalFileId, AbortToken);
        retrievedMetadata.Should().Contain("filename");
        retrievedMetadata.Should().Contain("customkey");
    }
}
