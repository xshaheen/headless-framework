// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Tus;
using Tests.TestSetup;
using TusAzureMetadata = Headless.Tus.Models.TusAzureMetadata;

namespace Tests.Concatenation;

[Collection<TusAzureFixture>]
public sealed class TusConcatenationTests(TusAzureFixture fixture)
    : TusConcatenationConformanceTestsBase<TusAzureStore>(fixture)
{
    private readonly BlobServiceClient _blobServiceClient = fixture.CreateBlobServiceClient();

    private readonly BlobContainerClient _containerClient = fixture
        .CreateBlobServiceClient()
        .GetBlobContainerClient(TusAzureFixture.ContainerName);

    private const string _ContainerName = TusAzureFixture.ContainerName;
    private const string _BlobPrefix = TusAzureFixture.BlobPrefix;

    /* CreatePartialFileAsync */

    [Fact]
    public async Task should_create_partial_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        // when
        var fileId = await Store.CreatePartialFileAsync(uploadLength, metadata, AbortToken);

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
        var fileId = await Store.CreatePartialFileAsync(uploadLength, metadata: null, AbortToken);

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

        var partialId1 = await Store.CreatePartialFileAsync(data1.Length, metadata: null, AbortToken);
        await using (var stream1 = new MemoryStream(data1))
        {
            await Store.AppendDataAsync(partialId1, stream1, AbortToken);
        }

        var partialId2 = await Store.CreatePartialFileAsync(data2.Length, metadata: null, AbortToken);
        await using (var stream2 = new MemoryStream(data2))
        {
            await Store.AppendDataAsync(partialId2, stream2, AbortToken);
        }

        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        // when
        var finalFileId = await Store.CreateFinalFileAsync([partialId1, partialId2], metadata, AbortToken);

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

    /* Data Integrity */

    [Fact]
    public async Task should_delete_partials_after_concat_when_enabled()
    {
        // given - a store with DeletePartialFilesOnConcat enabled on the same container
        var deletingStore = new TusAzureStore(
            _blobServiceClient,
            new TusAzureStoreOptions
            {
                ContainerName = _ContainerName,
                BlobPrefix = _BlobPrefix,
                DeletePartialFilesOnConcat = true,
            },
            loggerFactory: LoggerFactory
        );

        var data1 = Faker.Random.Bytes(300);
        var data2 = Faker.Random.Bytes(200);
        var partial1 = await deletingStore.CreatePartialFileAsync(data1.Length, metadata: null, AbortToken);
        var partial2 = await deletingStore.CreatePartialFileAsync(data2.Length, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(data1))
        {
            await deletingStore.AppendDataAsync(partial1, stream, AbortToken);
        }

        await using (var stream = new MemoryStream(data2))
        {
            await deletingStore.AppendDataAsync(partial2, stream, AbortToken);
        }

        // when
        var finalFileId = await deletingStore.CreateFinalFileAsync([partial1, partial2], metadata: null, AbortToken);

        // then - the final upload is durable and intact
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + finalFileId);
        await using var downloadStream = new MemoryStream();
        await blobClient.DownloadToAsync(downloadStream, AbortToken);
        downloadStream.ToArray().Should().BeEquivalentTo(data1.Concat(data2).ToArray());

        // and the partials are gone
        (await deletingStore.FileExistAsync(partial1, AbortToken))
            .Should()
            .BeFalse();
        (await deletingStore.FileExistAsync(partial2, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public override Task should_throw_when_create_final_file_with_empty_partials()
    {
        return base.should_throw_when_create_final_file_with_empty_partials();
    }

    [Fact]
    public override Task should_throw_when_create_final_file_with_nonexistent_partial()
    {
        return base.should_throw_when_create_final_file_with_nonexistent_partial();
    }

    [Fact]
    public override Task should_throw_when_create_final_file_with_regular_file_as_partial()
    {
        return base.should_throw_when_create_final_file_with_regular_file_as_partial();
    }

    [Fact]
    public override Task should_throw_when_create_final_file_with_incomplete_partial()
    {
        return base.should_throw_when_create_final_file_with_incomplete_partial();
    }

    [Fact]
    public override Task should_get_partial_file_info()
    {
        return base.should_get_partial_file_info();
    }

    [Fact]
    public override Task should_get_final_file_info()
    {
        return base.should_get_final_file_info();
    }

    [Fact]
    public override Task should_return_null_for_regular_file()
    {
        return base.should_return_null_for_regular_file();
    }

    [Fact]
    public override Task should_return_null_for_nonexistent_file()
    {
        return base.should_return_null_for_nonexistent_file();
    }

    [Fact]
    public override Task should_preserve_data_integrity_after_concatenation()
    {
        return base.should_preserve_data_integrity_after_concatenation();
    }

    [Fact]
    public override Task should_create_final_file_from_single_partial()
    {
        return base.should_create_final_file_from_single_partial();
    }

    [Fact]
    public override Task should_allow_same_partial_multiple_times_in_final_file()
    {
        return base.should_allow_same_partial_multiple_times_in_final_file();
    }

    [Fact]
    public override Task should_keep_partials_after_concat_by_default()
    {
        return base.should_keep_partials_after_concat_by_default();
    }

    [Fact]
    public override Task should_preserve_user_metadata_in_final_file()
    {
        return base.should_preserve_user_metadata_in_final_file();
    }
}
