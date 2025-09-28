using System.IO.Pipelines;
using System.Text.Unicode;
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
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions { ContainerName = _ContainerName, BlobPrefix = _BlobPrefix };
        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    /* Creation Store */

    // -- CreateFile

    [Fact]
    public async Task should_create_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        const string fileKey = "filename";

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
        properties.Value.Metadata.Should().ContainKey(fileKey);
        properties.Value.Metadata[fileKey].Should().Be(fileName);
        // Created date should be set correctly
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.CreatedDateKey);
        // Upload length should be set correctly
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(uploadLength.ToInvariantString());
    }

    // -- GetUploadMetadata

    [Fact]
    public async Task should_get_upload_metadata()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        // when
        var retrievedMetadata = await _store.GetUploadMetadataAsync(fileId, CancellationToken.None);

        // then
        retrievedMetadata.Should().NotBeNullOrEmpty();
        retrievedMetadata.Should().Be(metadata);
    }

    /* Creation Defer Length Store */

    // -- SetUploadLength

    [Fact]
    public async Task should_set_upload_length()
    {
        // given
        var initialUploadLength = -1L;
        var newUploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(initialUploadLength, metadata, CancellationToken.None);

        // when
        await _store.SetUploadLengthAsync(fileId, newUploadLength, CancellationToken.None);

        // then
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync();
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(newUploadLength.ToInvariantString());
    }

    /* Store Core */

    // -- FileExist

    [Fact]
    public async Task should_check_file_existence()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        const string nonExistentFileId = "nonexistentfileid";
        // when
        var exists = await _store.FileExistAsync(fileId, CancellationToken.None);
        var notExists = await _store.FileExistAsync(nonExistentFileId, CancellationToken.None);
        // then
        exists.Should().BeTrue();
        notExists.Should().BeFalse();
    }

    // -- GetUploadLength

    [Fact]
    public async Task should_get_upload_length()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        const string nonExistentFileId = "nonexistentfileid";
        // when
        var retrievedUploadLength = await _store.GetUploadLengthAsync(fileId, CancellationToken.None);
        var notExistsUploadLength = await _store.GetUploadLengthAsync(nonExistentFileId, CancellationToken.None);
        // then
        retrievedUploadLength.Should().Be(uploadLength);
        notExistsUploadLength.Should().BeNull();
    }

    // -- GetUploadOffset

    [Fact]
    public async Task should_get_upload_offset()
    {
        // given
        var uploadLength = Faker.Random.Number(1_000, 5_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var blockBlobClient = _containerClient.GetBlockBlobClient(_BlobPrefix + fileId);

        // Upload some blocks
        List<long> blockSizes = [500, 800, 200];
        List<string> blockIds = [];

        foreach (var size in blockSizes)
        {
            var blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            await using var stream = new MemoryStream(new byte[size]);
            await blockBlobClient.StageBlockAsync(blockId, stream);
            blockIds.Add(blockId);
        }

        await blockBlobClient.CommitBlockListAsync(blockIds, cancellationToken: CancellationToken.None);

        var expectedOffset = blockSizes.Sum();

        // when
        var uploadOffset = await _store.GetUploadOffsetAsync(fileId, CancellationToken.None);
        const string nonExistentFileId = "nonexistentfileid";
        var nonExistentOffset = await _store.GetUploadOffsetAsync(nonExistentFileId, CancellationToken.None);

        // then
        uploadOffset.Should().Be(expectedOffset);
        nonExistentOffset.Should().Be(0);
    }

    /* Readable Store */

    // -- GetFile

    [Fact]
    public async Task should_get_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        // when
        var tusFile = await _store.GetFileAsync(fileId, CancellationToken.None);
        const string nonExistentFileId = "nonexistentfileid";
        var nonExistentFile = await _store.GetFileAsync(nonExistentFileId, CancellationToken.None);

        // then
        tusFile.Should().NotBeNull();
        tusFile.Id.Should().Be(fileId);
        var metadataDict = await tusFile.GetMetadataAsync(CancellationToken.None);
        metadataDict.Should().ContainKey("filename");
        metadataDict["filename"].GetString(Encoding.UTF8).Should().Be(fileName);
        nonExistentFile.Should().BeNull();
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
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var existsBeforeDeletion = await blobClient.ExistsAsync();
        existsBeforeDeletion.Value.Should().BeTrue();

        // when
        await _store.DeleteFileAsync(fileId, CancellationToken.None);
        var existsAfterDeletion = await blobClient.ExistsAsync();
        const string nonExistentFileId = "nonexistentfileid";
        // Deleting a non-existent file should not throw
        var act = async () => await _store.DeleteFileAsync(nonExistentFileId, CancellationToken.None);

        // then
        existsAfterDeletion.Value.Should().BeFalse();
        await act.Should().NotThrowAsync();
    }

    /* Core Store - Steams */

    // -- AppendData

    [Fact]
    public async Task should_append_data_when_file_exists()
    {
        // given
        var uploadLength = Faker.Random.Number(5_000, 10_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        var dataToAppend = Faker.Random.Bytes(3_000);
        await using var stream = new MemoryStream(dataToAppend);

        // when
        var bytesAppended = await _store.AppendDataAsync(fileId, stream, CancellationToken.None);

        // then
        bytesAppended.Should().Be(dataToAppend.Length);

        var uploadOffset = await _store.GetUploadOffsetAsync(fileId, CancellationToken.None);
        uploadOffset.Should().Be(dataToAppend.Length);
    }

    [Fact]
    public async Task should_throw_when_append_data_to_nonexistent_file()
    {
        // given
        const string nonExistentFileId = "nonexistentfileid";
        var dataToAppend = Faker.Random.Bytes(3_000);
        await using var stream = new MemoryStream(dataToAppend);

        // when
        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await _store.AppendDataAsync(nonExistentFileId, stream, CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"File {nonExistentFileId} does not exist");
    }

    /* Core Store - Pipes */

    // -- AppendData

    [Fact]
    public async Task should_append_data_using_pipe_when_file_exists()
    {
        // given
        var uploadLength = Faker.Random.Number(5_000, 10_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var dataToAppend = Faker.Random.Bytes(3_000);
        var pipeReader = await _GetPipeReader(dataToAppend);

        // when
        var bytesAppended = await _store.AppendDataAsync(fileId, pipeReader, CancellationToken.None);

        // then
        bytesAppended.Should().Be(dataToAppend.Length);

        var uploadOffset = await _store.GetUploadOffsetAsync(fileId, CancellationToken.None);
        uploadOffset.Should().Be(dataToAppend.Length);
    }

    private static async Task<PipeReader> _GetPipeReader(byte[] dataToAppend)
    {
        var memoryStream = new MemoryStream();
        var pipe = PipeWriter.Create(memoryStream);
        await pipe.WriteAsync(dataToAppend);
        await pipe.FlushAsync();
        // Do not complete/dispose the pipe yet
        memoryStream.Position = 0;

        return PipeReader.Create(memoryStream);
    }

    [Fact]
    public async Task should_throw_when_append_data_using_pipe_to_nonexistent_file()
    {
        // given
        const string nonExistentFileId = "nonexistentfileid";
        var dataToAppend = Faker.Random.Bytes(3_000);
        var pipe = PipeWriter.Create(new MemoryStream());
        await pipe.WriteAsync(dataToAppend);

        // when
        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await _store.AppendDataAsync(nonExistentFileId, pipe.AsStream(), CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"File {nonExistentFileId} does not exist");
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
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var expiration = TimeSpan.FromHours(1);
        var expectedExpirationTime = DateTimeOffset.UtcNow.Add(expiration);
        // when
        await _store.SetExpirationAsync(fileId, expectedExpirationTime, CancellationToken.None);
        // then
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync();
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.ExpirationKey);
        var storedExpiration = DateTimeOffset.Parse(
            properties.Value.Metadata[TusAzureMetadata.ExpirationKey],
            CultureInfo.InvariantCulture
        );
        storedExpiration.Should().BeCloseTo(expectedExpirationTime, TimeSpan.FromMinutes(1));
    }

    // -- GetExpiration

    [Fact]
    public async Task should_get_expiration()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var expiration = TimeSpan.FromHours(1);
        var expectedExpirationTime = DateTimeOffset.UtcNow.Add(expiration);
        await _store.SetExpirationAsync(fileId, expectedExpirationTime, CancellationToken.None);
        // when
        var retrievedExpiration = await _store.GetExpirationAsync(fileId, CancellationToken.None);
        const string nonExistentFileId = "nonexistentfileid";
        var nonExistentExpiration = await _store.GetExpirationAsync(nonExistentFileId, CancellationToken.None);
        // then
        retrievedExpiration.Should().BeCloseTo(expectedExpirationTime, TimeSpan.FromMinutes(1));
        nonExistentExpiration.Should().BeNull();
    }

    // -- GetExpiredFiles

    [Fact]
    public async Task should_get_expired_files()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName1 = Faker.System.FileName();
        var metadata1 = $"filename {fileName1.ToBase64()}";
        var fileId1 = await _store.CreateFileAsync(uploadLength, metadata1, CancellationToken.None);
        var expiration1 = DateTimeOffset.UtcNow.AddMinutes(-10); // Expired 10 minutes ago
        await _store.SetExpirationAsync(fileId1, expiration1, CancellationToken.None);

        var fileName2 = Faker.System.FileName();
        var metadata2 = $"filename {fileName2.ToBase64()}";
        var fileId2 = await _store.CreateFileAsync(uploadLength, metadata2, CancellationToken.None);
        var expiration2 = DateTimeOffset.UtcNow.AddMinutes(10); // Expires in 10 minutes
        await _store.SetExpirationAsync(fileId2, expiration2, CancellationToken.None);

        var fileName3 = Faker.System.FileName();
        var metadata3 = $"filename {fileName3.ToBase64()}";
        var fileId3 = await _store.CreateFileAsync(uploadLength, metadata3, CancellationToken.None);
        var expiration3 = DateTimeOffset.UtcNow.AddMinutes(-5); // Expired 5 minutes ago
        await _store.SetExpirationAsync(fileId3, expiration3, CancellationToken.None);

        // when
        var expiredFiles = (await _store.GetExpiredFilesAsync(CancellationToken.None)).ToList();

        // then
        expiredFiles.Should().NotBeNull();
        expiredFiles.Should().Contain([fileId1, fileId3]);
        expiredFiles.Should().NotContain(fileId2);
    }
}
