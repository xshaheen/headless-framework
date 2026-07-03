// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Testing.Tests;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Tests.TestSetup;
using tusdotnet.Models;

namespace Tests.Flows;

/// <summary>
/// Pins the <see cref="TusAzureStoreOptions.EnableChunkSplitting"/> = <see langword="false"/>
/// append paths, which the rest of the suite never exercises: the whole PATCH body is staged as a
/// single block — streamed directly for seekable bodies (the store-direct fast path) and fully
/// buffered for non-seekable bodies (what an HTTP request body looks like).
/// </summary>
[Collection<TusAzureFixture>]
public sealed class NoChunkSplittingTests : TestBase
{
    private const string _ContainerName = "tusnosplit";
    private const string _BlobPrefix = "tusnosplit/";

    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private readonly BlobServiceClient _blobServiceClient;

    public NoChunkSplittingTests(TusAzureFixture fixture)
    {
        _blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions
        {
            ContainerName = _ContainerName,
            BlobPrefix = _BlobPrefix,
            EnableChunkSplitting = false,
        };

        _containerClient = _blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(_blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    private TusAzureStore _CreateStore(int maxNoSplitBufferSize)
    {
        return new TusAzureStore(
            _blobServiceClient,
            new TusAzureStoreOptions
            {
                ContainerName = _ContainerName,
                BlobPrefix = _BlobPrefix,
                EnableChunkSplitting = false,
                MaxNoSplitBufferSize = maxNoSplitBufferSize,
            },
            loggerFactory: LoggerFactory
        );
    }

    [Fact]
    public async Task should_stage_seekable_body_as_a_single_block()
    {
        // given
        var content = Faker.Random.Bytes(5_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when - seekable fast path: the stream goes to StageBlock directly, no buffering
        await using (var body = new MemoryStream(content))
        {
            var written = await _store.AppendDataAsync(fileId, body, AbortToken);
            written.Should().Be(content.Length);
        }

        // then - exactly one committed block carrying the whole body
        await _AssertSingleBlockUploadAsync(fileId, content);
    }

    [Fact]
    public async Task should_stage_non_seekable_body_as_a_single_block()
    {
        // given
        var content = Faker.Random.Bytes(5_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when - a non-seekable body (the HTTP request-body shape) is buffered, then staged once
        await using (var body = new NonSeekableReadStream(content))
        {
            var written = await _store.AppendDataAsync(fileId, body, AbortToken);
            written.Should().Be(content.Length);
        }

        // then
        await _AssertSingleBlockUploadAsync(fileId, content);
    }

    [Fact]
    public async Task should_resume_across_multiple_single_block_appends()
    {
        // given - each PATCH contributes one block; the blocks concatenate in order
        var chunk1 = Faker.Random.Bytes(3_000);
        var chunk2 = Faker.Random.Bytes(2_000);
        var fileId = await _store.CreateFileAsync(chunk1.Length + chunk2.Length, metadata: null, AbortToken);

        // when
        await using (var body = new MemoryStream(chunk1))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        await using (var body = new MemoryStream(chunk2))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // then
        (await _store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(chunk1.Length + chunk2.Length);

        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloaded = new MemoryStream();
        await blobClient.DownloadToAsync(downloaded, AbortToken);
        downloaded.ToArray().Should().BeEquivalentTo(chunk1.Concat(chunk2).ToArray());
    }

    [Fact]
    public async Task should_reject_non_seekable_body_longer_than_declared_upload_length()
    {
        // given - a non-seekable body larger than the declared Upload-Length
        var content = Faker.Random.Bytes(5_000);
        var fileId = await _store.CreateFileAsync(uploadLength: 3_000, metadata: null, AbortToken);

        // when - the no-split path buffers incrementally and must reject before staging
        await using var body = new NonSeekableReadStream(content);
        var act = async () => await _store.AppendDataAsync(fileId, body, AbortToken);

        // then - rejected, and nothing committed (offset stays 0)
        await act.Should().ThrowAsync<TusStoreException>();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_reject_deferred_length_body_exceeding_the_no_split_buffer_cap()
    {
        // given - a deferred-length upload (no declared length, so the too-much-data guard is a no-op)
        // against a store whose no-split buffer cap is deliberately tiny: the cap is the only memory bound.
        var store = _CreateStore(maxNoSplitBufferSize: 4_096);
        var fileId = await store.CreateFileAsync(uploadLength: -1, metadata: null, AbortToken);
        var content = Faker.Random.Bytes(8_192);

        // when
        await using var body = new NonSeekableReadStream(content);
        var act = async () => await store.AppendDataAsync(fileId, body, AbortToken);

        // then - rejected by the cap (not the declared-length guard), nothing committed
        (await act.Should().ThrowAsync<TusStoreException>()).WithMessage("*MaxNoSplitBufferSize*");
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);
    }

    private async Task _AssertSingleBlockUploadAsync(string fileId, byte[] expectedContent)
    {
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(expectedContent.Length);

        var blockBlobClient = _containerClient.GetBlockBlobClient(_BlobPrefix + fileId);
        var blockList = await blockBlobClient.GetBlockListAsync(
            BlockListTypes.Committed,
            cancellationToken: AbortToken
        );
        blockList.Value.CommittedBlocks.Should().HaveCount(1);

        await using var downloaded = new MemoryStream();
        await _containerClient.GetBlobClient(_BlobPrefix + fileId).DownloadToAsync(downloaded, AbortToken);
        downloaded.ToArray().Should().BeEquivalentTo(expectedContent);
    }

    /// <summary>Forward-only read stream, the shape of an ASP.NET Core request body.</summary>
    private sealed class NonSeekableReadStream(byte[] data) : Stream
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(count, data.Length - _position);
            data.AsSpan(_position, take).CopyTo(buffer.AsSpan(offset, take));
            _position += take;

            return take;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
