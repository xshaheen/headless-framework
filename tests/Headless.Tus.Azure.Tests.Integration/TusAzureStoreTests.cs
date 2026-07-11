using System.IO.Pipelines;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Headless.Testing.Tests;
using Headless.Tus;
using Tests.TestSetup;
using TusAzureMetadata = Headless.Tus.Models.TusAzureMetadata;

namespace Tests;

[Collection<TusAzureFixture>]
public sealed class TusAzureStoreTests : TestBase
{
    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private const string _ContainerName = "tuscontainer";
    private const string _BlobPrefix = "tusfiles/";

    public TusAzureStoreTests(TusAzureFixture fixture)
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

        // when
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

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

    [Fact]
    public async Task should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys()
    {
        // given - a non-ASCII filename and keys tusdotnet clients legitimately send; the spec
        // requires HEAD to echo Upload-Metadata exactly as the client specified it
        const string arabicFileName = "تقرير-2026.pdf";
        var metadata = $"FileName {arabicFileName.ToBase64()},is_confidential,x-trace-id {"abc123".ToBase64()}";

        // when - creation must not fail on the non-ASCII value (Azure metadata values are ASCII-only;
        // the raw TUS string is stored verbatim instead of decoded)
        var fileId = await _store.CreateFileAsync(500, metadata, AbortToken);

        // then - byte-for-byte round-trip
        var retrievedMetadata = await _store.GetUploadMetadataAsync(fileId, AbortToken);
        retrievedMetadata.Should().Be(metadata);

        // and the decoded view surfaces the original keys and UTF-8 values
        var tusFile = await _store.GetFileAsync(fileId, AbortToken);
        var decoded = await tusFile!.GetMetadataAsync(AbortToken);
        decoded.Should().ContainKey("FileName");
        decoded["FileName"].GetString(Encoding.UTF8).Should().Be(arabicFileName);
        decoded.Should().ContainKey("is_confidential");
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
        var fileId = await _store.CreateFileAsync(initialUploadLength, metadata, CancellationToken.None);

        // the -1 sentinel must not be persisted: tusdotnet decides between "Upload-Length" and
        // "Upload-Defer-Length: 1" in HEAD responses based on GetUploadLengthAsync being null
        (await _store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .BeNull();

        // when
        await _store.SetUploadLengthAsync(fileId, newUploadLength, CancellationToken.None);

        // then
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.Metadata.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        properties.Value.Metadata[TusAzureMetadata.UploadLengthKey].Should().Be(newUploadLength.ToInvariantString());
    }

    [Fact]
    public async Task should_upload_data_before_upload_length_is_known()
    {
        // given - the standard tus defer-length flow: data PATCHes arrive BEFORE the client
        // declares Upload-Length (tus-js-client streams of unknown size do exactly this)
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(-1L, metadata: null, AbortToken);

        // when - append while the length is still unknown
        await using (var stream = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, stream, AbortToken);
        }

        // then - the data is accepted and the offset advances
        (await _store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(content.Length);

        // and when - the client declares the final length on a later request
        await _store.SetUploadLengthAsync(fileId, content.Length, AbortToken);

        // then - the upload reads as complete
        (await _store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .Be(content.Length);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
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
        var nonExistentFileId = Guid.NewGuid().ToString("n");
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
        var nonExistentFileId = Guid.NewGuid().ToString("n");
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
            await blockBlobClient.StageBlockAsync(blockId, stream, cancellationToken: AbortToken);
            blockIds.Add(blockId);
        }

        await blockBlobClient.CommitBlockListAsync(blockIds, cancellationToken: CancellationToken.None);

        var expectedOffset = blockSizes.Sum();

        // when
        var uploadOffset = await _store.GetUploadOffsetAsync(fileId, CancellationToken.None);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
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
        var nonExistentFileId = Guid.NewGuid().ToString("n");
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
        var existsBeforeDeletion = await blobClient.ExistsAsync(AbortToken);
        existsBeforeDeletion.Value.Should().BeTrue();

        // when
        await _store.DeleteFileAsync(fileId, CancellationToken.None);
        var existsAfterDeletion = await blobClient.ExistsAsync(AbortToken);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
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
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var dataToAppend = Faker.Random.Bytes(3_000);
        await using var stream = new MemoryStream(dataToAppend);

        // when
        var act = async () => await _store.AppendDataAsync(nonExistentFileId, stream, CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<tusdotnet.Models.TusStoreException>()
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
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var dataToAppend = Faker.Random.Bytes(3_000);
        var pipe = PipeWriter.Create(new MemoryStream());
        await pipe.WriteAsync(dataToAppend, AbortToken);

        // when
        var act = async () => await _store.AppendDataAsync(nonExistentFileId, pipe.AsStream(), CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<tusdotnet.Models.TusStoreException>()
            .WithMessage($"File {nonExistentFileId} does not exist");
    }

    // -- AppendData: client disconnect must persist received bytes

    [Fact]
    public async Task should_persist_received_bytes_when_client_disconnects_mid_stream()
    {
        // given - a body that serves 1000 bytes, then simulates a disconnect: tusdotnet cancels the
        // request token when the client goes away, and the next read observes it
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(4_000, metadata: null, AbortToken);

        using var cts = new CancellationTokenSource();
        await using var body = new DisconnectingReadStream(content, cts);

        // when
        var written = await _store.AppendDataAsync(fileId, body, cts.Token);

        // then - the tus spec: "the Server SHOULD always attempt to store as much of the received
        // data as possible"; the client resumes from these bytes instead of re-uploading them
        written.Should().Be(content.Length);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    [Fact]
    public async Task should_persist_received_bytes_when_pipe_read_is_cancelled()
    {
        // given - 1000 bytes buffered in the pipe, then the pending read is cancelled (the pipe
        // analog of a client disconnect)
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(4_000, metadata: null, AbortToken);

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(content, AbortToken);
        await pipe.Writer.FlushAsync(AbortToken);
        pipe.Reader.CancelPendingRead();

        // when
        var written = await _store.AppendDataAsync(fileId, pipe.Reader, AbortToken);

        // then
        written.Should().Be(content.Length);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    /// <summary>
    /// Serves the given bytes, then mimics tusdotnet's client-disconnect behavior: the request
    /// token gets cancelled and the in-flight read surfaces <see cref="OperationCanceledException"/>.
    /// </summary>
    private sealed class DisconnectingReadStream(byte[] data, CancellationTokenSource cts) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_position >= data.Length)
            {
                await cts.CancelAsync();

                throw new OperationCanceledException(cts.Token);
            }

            var count = Math.Min(buffer.Length, data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;

            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /* Checksum Store */

    // -- GetSupportedAlgorithms

    [Fact]
    public async Task should_return_supported_checksum_algorithms()
    {
        // when
        var algorithms = (await _store.GetSupportedAlgorithmsAsync(CancellationToken.None)).ToList();

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
        var fileId = await _store.CreateFileAsync(dataToAppend.Length, metadata: null, CancellationToken.None);

        await using var stream = new MemoryStream(dataToAppend);
        await _store.AppendDataAsync(fileId, stream, CancellationToken.None);

        // when - the digest arrives afterwards (trailer flow) and matches
        var checksum = SHA256.HashData(dataToAppend);
        var isValid = await _store.VerifyChecksumAsync(fileId, "sha256", checksum, CancellationToken.None);

        // then - the store hashes the committed chunk on demand and confirms it
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_no_chunk_was_appended()
    {
        // given - a freshly created upload with no appended data
        var fileId = await _store.CreateFileAsync(1_000, metadata: null, CancellationToken.None);

        // when - verify is called without any prior append (nothing to verify)
        var anyChecksum = SHA256.HashData(Faker.Random.Bytes(100));
        var isValid = await _store.VerifyChecksumAsync(fileId, "sha256", anyChecksum, CancellationToken.None);

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
        var isValid = await _store.VerifyChecksumAsync(nonExistentFileId, "sha256", checksum, CancellationToken.None);

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
        var fileId = await _store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var expiration = TimeSpan.FromHours(1);
        var expectedExpirationTime = DateTimeOffset.UtcNow.Add(expiration);
        // when
        await _store.SetExpirationAsync(fileId, expectedExpirationTime, CancellationToken.None);
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
        var nonExistentFileId = Guid.NewGuid().ToString("n");
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
