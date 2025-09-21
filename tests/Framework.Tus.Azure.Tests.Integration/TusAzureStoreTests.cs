using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Framework.Testing.Tests;
using Framework.Tus.Models;
using Framework.Tus.Options;
using Framework.Tus.Services;
using Microsoft.Extensions.Logging;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(TusAzureFixture))]
public sealed class TusAzureStoreTests : TestBase
{
    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private const string _ContainerName = "tuscontainer";
    private const string _BlobPrefix = "tusfiles/";

    public TusAzureStoreTests(TusAzureFixture fixture, ITestOutputHelper output)
        : base(output)
    {
        var connectionString = fixture.Container.GetConnectionString();
        var clientOptions = new SpecializedBlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03);

        var storeOptions = new TusAzureStoreOptions
        {
            ConnectionString = connectionString,
            BlobClientOptions = clientOptions,
            ContainerName = _ContainerName,
            BlobPrefix = _BlobPrefix,
        };

        var blobServiceClient = new BlobServiceClient(connectionString, clientOptions);
        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(storeOptions, LoggerFactory.CreateLogger<TusAzureStore>());
    }

    // CreateFileAsync

    [Fact]
    public async Task should_create_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        const string tusFileKey = TusAzureMetadata.TusMetadataPrefix + "filename";

        // when
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        // then
        fileId.Should().NotBeNullOrEmpty();
        // Blob should exist
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var exists = await blobClient.ExistsAsync();
        exists.Value.Should().BeTrue();
        var properties = await blobClient.GetPropertiesAsync();
        // Filename should be set correctly
        properties.Value.Metadata.Should().ContainKey(tusFileKey);
        properties.Value.Metadata[tusFileKey].Should().Be(fileName);
        // Created date should be set correctly
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.CreatedDateKey);
        // Upload length should be set correctly
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(uploadLength.ToInvariantString());
        // New files should have 0 blocks committed
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.BlockCountKey);
        properties.Value.Metadata[TusAzureMetadata.BlockCountKey].Should().Be("0");
    }
}
