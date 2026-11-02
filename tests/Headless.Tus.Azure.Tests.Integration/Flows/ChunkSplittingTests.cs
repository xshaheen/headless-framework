// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;

namespace Tests.Flows;

/// <summary>
/// Pins the chunk-splitting <c>Stream</c> append path against a body that returns short reads, as an
/// ASP.NET Core request body does: each staged block must be a full chunk, not one block per read.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class ChunkSplittingTests : TestBase
{
    private const string _ContainerName = "tussplit";
    private const string _BlobPrefix = "tussplit/";
    private const int _ChunkSize = 4_096;
    private const int _ReadSize = 512;

    private readonly TusAzureStore _store;
    private readonly TusAzureContainerProvisioner _provisioner;
    private readonly BlobContainerClient _containerClient;

    public ChunkSplittingTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions
        {
            ContainerName = _ContainerName,
            BlobPrefix = _BlobPrefix,
            EnableChunkSplitting = true,
            BlobDefaultChunkSize = _ChunkSize,
            BlobMaxChunkSize = _ChunkSize,
        };

        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
        _provisioner = new TusAzureContainerProvisioner(blobServiceClient, storeOptions);
    }

    // The store does not create its container; the deploy-time provisioner does.
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _provisioner.ProvisionAsync(AbortToken);
    }

    [Fact]
    public async Task should_stage_full_chunks_when_the_body_returns_short_reads()
    {
        // given
        var content = Faker.Random.Bytes(10_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when
        await using (var body = new TrickleReadStream(content, _ReadSize))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // then - 4096 + 4096 + 1808, not one block per 512-byte read
        (await _GetCommittedBlockSizesAsync(fileId))
            .Should()
            .Equal(_ChunkSize, _ChunkSize, 1_808);
        (await _DownloadAsync(fileId)).Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task should_stage_the_received_prefix_when_the_client_disconnects_mid_chunk()
    {
        // given
        var content = Faker.Random.Bytes(10_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when - the body throws a cancellation after 5000 bytes, partway through the second chunk
        await using (var body = new TrickleReadStream(content, _ReadSize, cancelAfterBytes: 5_000))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // then - the full first chunk and the partial second one both land
        (await _store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(5_000);
        (await _GetCommittedBlockSizesAsync(fileId)).Should().Equal(_ChunkSize, 904);
        (await _DownloadAsync(fileId)).Should().BeEquivalentTo(content.AsSpan(0, 5_000).ToArray());
    }

    private async Task<long[]> _GetCommittedBlockSizesAsync(string fileId)
    {
        var blockList = await _containerClient
            .GetBlockBlobClient(_BlobPrefix + fileId)
            .GetBlockListAsync(BlockListTypes.Committed, cancellationToken: AbortToken);

        return [.. blockList.Value.CommittedBlocks.Select(block => block.SizeLong)];
    }

    private async Task<byte[]> _DownloadAsync(string fileId)
    {
        await using var downloaded = new MemoryStream();
        await _containerClient.GetBlobClient(_BlobPrefix + fileId).DownloadToAsync(downloaded, AbortToken);

        return downloaded.ToArray();
    }

    /// <summary>
    /// Forward-only stream that returns at most <paramref name="readSize"/> bytes per read and, when
    /// <paramref name="cancelAfterBytes"/> is set, throws a cancellation once that many bytes were read.
    /// </summary>
    private sealed class TrickleReadStream(byte[] data, int readSize, int? cancelAfterBytes = null) : Stream
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
            var end = cancelAfterBytes ?? data.Length;

            if (_position >= end && cancelAfterBytes is not null)
            {
                throw new OperationCanceledException();
            }

            var take = Math.Min(Math.Min(count, readSize), end - _position);
            data.AsSpan(_position, take).CopyTo(buffer.AsSpan(offset, take));
            _position += take;

            return take;
        }

        public override void Flush() { }

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
}
