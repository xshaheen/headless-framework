using Azure.Storage.Blobs;
using Framework.Testing.Tests;
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
    private const string _ContainerName = "tus_container";

    public TusAzureStoreTests(TusAzureFixture fixture, ITestOutputHelper output)
        : base(output)
    {
        var connectionString = fixture.Container.GetConnectionString();
        var blobServiceClient = new BlobServiceClient(connectionString);
        var options = new TusAzureStoreOptions { ConnectionString = connectionString, ContainerName = _ContainerName };
        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(options, LoggerFactory.CreateLogger<TusAzureStore>());
    }

    // CreateFileAsync

    [Fact]
    public async Task should_create_file()
    {
        // Arrange
        const long uploadLength = 100L;
        const string metadata = "filename dGVzdC50eHQ=";

        // Act
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        // Assert
        fileId.Should().NotBeNullOrEmpty();
        var blobClient = _containerClient.GetBlobClient(fileId);
        var exists = await blobClient.ExistsAsync();
        exists.Value.Should().BeTrue();
    }
}
