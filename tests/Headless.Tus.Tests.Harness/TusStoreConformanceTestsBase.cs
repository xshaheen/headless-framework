// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using tusdotnet.Interfaces;

namespace Tests;

/// <summary>
/// Store-interface behavior every tus store must share: creation, deferred length, offsets, reads, appends through
/// both <see cref="Stream" /> and <see cref="PipeReader" />, disconnect persistence, and expiration bookkeeping.
/// </summary>
public abstract class TusStoreConformanceTestsBase<TStore>(ITusConformanceBackend<TStore> backend)
    : TusConformanceTestsBase<TStore>(backend)
    where TStore : class,
        ITusStore,
        ITusPipelineStore,
        ITusCreationStore,
        ITusCreationDeferLengthStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusReadableStore,
        ITusConcatenationStore
{
    /* Creation Store */

    // -- GetUploadMetadata

    public virtual async Task should_get_upload_metadata()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        // when
        var retrievedMetadata = await Store.GetUploadMetadataAsync(fileId, CancellationToken.None);

        // then
        retrievedMetadata.Should().NotBeNullOrEmpty();
        retrievedMetadata.Should().Be(metadata);
    }

    public virtual async Task should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys()
    {
        // given - a non-ASCII filename and keys tusdotnet clients legitimately send; the spec
        // requires HEAD to echo Upload-Metadata exactly as the client specified it
        const string arabicFileName = "تقرير-2026.pdf";
        var metadata = $"FileName {arabicFileName.ToBase64()},is_confidential,x-trace-id {"abc123".ToBase64()}";

        // when - creation must not fail on the non-ASCII value (a store whose native metadata is
        // ASCII-only stores the raw TUS string verbatim instead of decoding it)
        var fileId = await Store.CreateFileAsync(500, metadata, AbortToken);

        // then - byte-for-byte round-trip
        var retrievedMetadata = await Store.GetUploadMetadataAsync(fileId, AbortToken);
        retrievedMetadata.Should().Be(metadata);

        // and the decoded view surfaces the original keys and UTF-8 values
        var tusFile = await Store.GetFileAsync(fileId, AbortToken);
        var decoded = await tusFile!.GetMetadataAsync(AbortToken);
        decoded.Should().ContainKey("FileName");
        decoded["FileName"].GetString(Encoding.UTF8).Should().Be(arabicFileName);
        decoded.Should().ContainKey("is_confidential");
    }

    /* Creation Defer Length Store */

    public virtual async Task should_upload_data_before_upload_length_is_known()
    {
        // given - the standard tus defer-length flow: data PATCHes arrive BEFORE the client
        // declares Upload-Length (tus-js-client streams of unknown size do exactly this)
        var content = Faker.Random.Bytes(1_000);
        var fileId = await Store.CreateFileAsync(-1L, metadata: null, AbortToken);

        // when - append while the length is still unknown
        await using (var stream = new MemoryStream(content))
        {
            await Store.AppendDataAsync(fileId, stream, AbortToken);
        }

        // then - the data is accepted and the offset advances
        (await Store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(content.Length);

        // and when - the client declares the final length on a later request
        await Store.SetUploadLengthAsync(fileId, content.Length, AbortToken);

        // then - the upload reads as complete
        (await Store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .Be(content.Length);
        (await Store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    /* Store Core */

    // -- FileExist

    public virtual async Task should_check_file_existence()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        // when
        var exists = await Store.FileExistAsync(fileId, CancellationToken.None);
        var notExists = await Store.FileExistAsync(nonExistentFileId, CancellationToken.None);
        // then
        exists.Should().BeTrue();
        notExists.Should().BeFalse();
    }

    // -- GetUploadLength

    public virtual async Task should_get_upload_length()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        // when
        var retrievedUploadLength = await Store.GetUploadLengthAsync(fileId, CancellationToken.None);
        var notExistsUploadLength = await Store.GetUploadLengthAsync(nonExistentFileId, CancellationToken.None);
        // then
        retrievedUploadLength.Should().Be(uploadLength);
        notExistsUploadLength.Should().BeNull();
    }

    /* Readable Store */

    // -- GetFile

    public virtual async Task should_get_file()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        // when
        var tusFile = await Store.GetFileAsync(fileId, CancellationToken.None);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var nonExistentFile = await Store.GetFileAsync(nonExistentFileId, CancellationToken.None);

        // then
        tusFile.Should().NotBeNull();
        tusFile.Id.Should().Be(fileId);
        var metadataDict = await tusFile.GetMetadataAsync(CancellationToken.None);
        metadataDict.Should().ContainKey("filename");
        metadataDict["filename"].GetString(Encoding.UTF8).Should().Be(fileName);
        nonExistentFile.Should().BeNull();
    }

    /* Core Store - Steams */

    // -- AppendData

    public virtual async Task should_append_data_when_file_exists()
    {
        // given
        var uploadLength = Faker.Random.Number(5_000, 10_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);

        var dataToAppend = Faker.Random.Bytes(3_000);
        await using var stream = new MemoryStream(dataToAppend);

        // when
        var bytesAppended = await Store.AppendDataAsync(fileId, stream, CancellationToken.None);

        // then
        bytesAppended.Should().Be(dataToAppend.Length);

        var uploadOffset = await Store.GetUploadOffsetAsync(fileId, CancellationToken.None);
        uploadOffset.Should().Be(dataToAppend.Length);
    }

    public virtual async Task should_throw_when_append_data_to_nonexistent_file()
    {
        // given
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var dataToAppend = Faker.Random.Bytes(3_000);
        await using var stream = new MemoryStream(dataToAppend);

        // when
        var act = async () => await Store.AppendDataAsync(nonExistentFileId, stream, CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<tusdotnet.Models.TusStoreException>()
            .WithMessage($"File {nonExistentFileId} does not exist");
    }

    /* Core Store - Pipes */

    // -- AppendData

    public virtual async Task should_append_data_using_pipe_when_file_exists()
    {
        // given
        var uploadLength = Faker.Random.Number(5_000, 10_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var dataToAppend = Faker.Random.Bytes(3_000);
        var pipeReader = await _GetPipeReader(dataToAppend);

        // when
        var bytesAppended = await Store.AppendDataAsync(fileId, pipeReader, CancellationToken.None);

        // then
        bytesAppended.Should().Be(dataToAppend.Length);

        var uploadOffset = await Store.GetUploadOffsetAsync(fileId, CancellationToken.None);
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

    public virtual async Task should_throw_when_append_data_using_pipe_to_nonexistent_file()
    {
        // given
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var dataToAppend = Faker.Random.Bytes(3_000);
        var pipe = PipeWriter.Create(new MemoryStream());
        await pipe.WriteAsync(dataToAppend, AbortToken);

        // when
        var act = async () => await Store.AppendDataAsync(nonExistentFileId, pipe.AsStream(), CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<tusdotnet.Models.TusStoreException>()
            .WithMessage($"File {nonExistentFileId} does not exist");
    }

    // -- AppendData: client disconnect must persist received bytes

    public virtual async Task should_persist_received_bytes_when_client_disconnects_mid_stream()
    {
        // given - a body that serves 1000 bytes, then simulates a disconnect: tusdotnet cancels the
        // request token when the client goes away, and the next read observes it
        var content = Faker.Random.Bytes(1_000);
        var fileId = await Store.CreateFileAsync(4_000, metadata: null, AbortToken);

        using var cts = new CancellationTokenSource();
        await using var body = new DisconnectingReadStream(content, cts);

        // when
        var written = await Store.AppendDataAsync(fileId, body, cts.Token);

        // then - the tus spec: "the Server SHOULD always attempt to store as much of the received
        // data as possible"; the client resumes from these bytes instead of re-uploading them
        written.Should().Be(content.Length);
        (await Store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    public virtual async Task should_persist_received_bytes_when_pipe_read_is_cancelled()
    {
        // given - 1000 bytes buffered in the pipe, then the pending read is cancelled (the pipe
        // analog of a client disconnect)
        var content = Faker.Random.Bytes(1_000);
        var fileId = await Store.CreateFileAsync(4_000, metadata: null, AbortToken);

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(content, AbortToken);
        await pipe.Writer.FlushAsync(AbortToken);
        pipe.Reader.CancelPendingRead();

        // when
        var written = await Store.AppendDataAsync(fileId, pipe.Reader, AbortToken);

        // then
        written.Should().Be(content.Length);
        (await Store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /* Expiration Store */

    // -- GetExpiration

    public virtual async Task should_get_expiration()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, CancellationToken.None);
        var expiration = TimeSpan.FromHours(1);
        var expectedExpirationTime = DateTimeOffset.UtcNow.Add(expiration);
        await Store.SetExpirationAsync(fileId, expectedExpirationTime, CancellationToken.None);
        // when
        var retrievedExpiration = await Store.GetExpirationAsync(fileId, CancellationToken.None);
        var nonExistentFileId = Guid.NewGuid().ToString("n");
        var nonExistentExpiration = await Store.GetExpirationAsync(nonExistentFileId, CancellationToken.None);
        // then
        retrievedExpiration.Should().BeCloseTo(expectedExpirationTime, TimeSpan.FromMinutes(1));
        nonExistentExpiration.Should().BeNull();
    }

    // -- GetExpiredFiles

    public virtual async Task should_get_expired_files()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var fileName1 = Faker.System.FileName();
        var metadata1 = $"filename {fileName1.ToBase64()}";
        var fileId1 = await Store.CreateFileAsync(uploadLength, metadata1, CancellationToken.None);
        var expiration1 = DateTimeOffset.UtcNow.AddMinutes(-10); // Expired 10 minutes ago
        await Store.SetExpirationAsync(fileId1, expiration1, CancellationToken.None);

        var fileName2 = Faker.System.FileName();
        var metadata2 = $"filename {fileName2.ToBase64()}";
        var fileId2 = await Store.CreateFileAsync(uploadLength, metadata2, CancellationToken.None);
        var expiration2 = DateTimeOffset.UtcNow.AddMinutes(10); // Expires in 10 minutes
        await Store.SetExpirationAsync(fileId2, expiration2, CancellationToken.None);

        var fileName3 = Faker.System.FileName();
        var metadata3 = $"filename {fileName3.ToBase64()}";
        var fileId3 = await Store.CreateFileAsync(uploadLength, metadata3, CancellationToken.None);
        var expiration3 = DateTimeOffset.UtcNow.AddMinutes(-5); // Expired 5 minutes ago
        await Store.SetExpirationAsync(fileId3, expiration3, CancellationToken.None);

        // when
        var expiredFiles = (await Store.GetExpiredFilesAsync(CancellationToken.None)).ToList();

        // then
        expiredFiles.Should().NotBeNull();
        expiredFiles.Should().Contain([fileId1, fileId3]);
        expiredFiles.Should().NotContain(fileId2);
    }
}
