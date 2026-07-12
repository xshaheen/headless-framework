// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus;
using Tests.TestSetup;
using tusdotnet.Helpers;
using TusAzureMetadata = Headless.Tus.Models.TusAzureMetadata;

namespace Tests.Checksum;

/// <summary>
/// Exercises the checksum-<em>trailer</em> flow: the digest arrives after the body, so
/// <c>AppendDataAsync</c> sees no checksum info (tusdotnet only wraps the body in a
/// ChecksumAware stream for the header flow) and commits immediately. <c>VerifyChecksumAsync</c>
/// must then hash the committed chunk range on demand and roll the chunk back on mismatch or on
/// tusdotnet's faulty-trailer fallback sentinel.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class ChecksumTrailerTests : TestBase
{
    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private const string _ContainerName = "tustrailer";
    private const string _BlobPrefix = "tustrailer/";

    public ChecksumTrailerTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        // A small chunk size makes each appended chunk span several Azure blocks, so the rollback
        // path has to align the block-list prefix across multiple blocks.
        var storeOptions = new TusAzureStoreOptions
        {
            ContainerName = _ContainerName,
            BlobPrefix = _BlobPrefix,
            BlobDefaultChunkSize = 256,
        };

        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
    }

    [Fact]
    public async Task verify_succeeds_for_committed_chunk_with_correct_trailer_digest()
    {
        // given - a plain append (no ChecksumAware wrapper), i.e. what a trailer PATCH looks like
        var content = Faker.Random.Bytes(1_500);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        await using (var body = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // when - the trailer digest arrives and matches
        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", SHA256.HashData(content), AbortToken);

        // then - the chunk stays committed
        verified.Should().BeTrue();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    [Fact]
    public async Task verify_rolls_back_committed_chunk_on_trailer_digest_mismatch()
    {
        // given - one verified chunk followed by a second chunk whose trailer digest is wrong
        var chunk1 = Faker.Random.Bytes(700);
        var chunk2 = Faker.Random.Bytes(500);
        var fileId = await _store.CreateFileAsync(chunk1.Length + chunk2.Length, metadata: null, AbortToken);

        await using (var body = new MemoryStream(chunk1))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        await using (var body = new MemoryStream(chunk2))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        var wrongDigest = SHA256.HashData(Faker.Random.Bytes(500));

        // when
        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", wrongDigest, AbortToken);

        // then - only the second chunk is discarded; the upload resumes from the first chunk's end
        verified.Should().BeFalse();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(chunk1.Length);

        // and the surviving content is exactly the first chunk
        var tusFile = await _store.GetFileAsync(fileId, AbortToken);
        await using var contentStream = await tusFile!.GetContentAsync(AbortToken);
        await using var downloaded = new MemoryStream();
        await contentStream.CopyToAsync(downloaded, AbortToken);
        downloaded.ToArray().Should().BeEquivalentTo(chunk1);
    }

    [Fact]
    public async Task verify_discards_committed_chunk_on_faulty_trailer_fallback()
    {
        // given - a committed chunk whose trailer never (validly) arrived; tusdotnet then calls
        // VerifyChecksumAsync with its internal fallback sentinel to force a discard
        var content = Faker.Random.Bytes(900);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        await using (var body = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        var (algorithm, hash) = _GetTrailerFallbackSentinel();

        // when - note: tusdotnet passes the request token, which is cancelled on disconnect; the
        // discard must complete anyway
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var verified = await _store.VerifyChecksumAsync(fileId, algorithm, hash, cancelled.Token);

        // then - the chunk is rolled back
        verified.Should().BeFalse();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task verify_rolls_back_only_the_last_chunk_on_fallback()
    {
        // given - a good first chunk, then a second chunk whose trailer was faulty
        var chunk1 = Faker.Random.Bytes(600);
        var chunk2 = Faker.Random.Bytes(400);
        var fileId = await _store.CreateFileAsync(chunk1.Length + chunk2.Length, metadata: null, AbortToken);

        await using (var body = new MemoryStream(chunk1))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        await using (var body = new MemoryStream(chunk2))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        var (algorithm, hash) = _GetTrailerFallbackSentinel();

        // when
        var verified = await _store.VerifyChecksumAsync(fileId, algorithm, hash, AbortToken);

        // then
        verified.Should().BeFalse();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(chunk1.Length);
    }

    [Fact]
    public async Task should_not_roll_back_a_verified_chunk_after_a_zero_byte_append()
    {
        // given - a committed and verified chunk, then a PATCH that delivers zero bytes (client
        // disconnected before sending data) with a declared checksum trailer that never arrives.
        // The zero-byte append must refresh the chunk-tracking metadata; otherwise the fallback
        // sentinel would act on the PREVIOUS append's rollback point and destroy verified data.
        var content = Faker.Random.Bytes(800);
        var fileId = await _store.CreateFileAsync(content.Length + 100, metadata: null, AbortToken);

        await using (var body = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        (await _store.VerifyChecksumAsync(fileId, "sha256", SHA256.HashData(content), AbortToken)).Should().BeTrue();

        await using (var empty = new MemoryStream([]))
        {
            (await _store.AppendDataAsync(fileId, empty, AbortToken)).Should().Be(0);
        }

        var (algorithm, hash) = _GetTrailerFallbackSentinel();

        // when - tusdotnet discards the (empty) chunk via the fallback sentinel
        var verified = await _store.VerifyChecksumAsync(fileId, algorithm, hash, AbortToken);

        // then - nothing to discard; the previously verified chunk survives
        verified.Should().BeFalse();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    [Fact]
    public async Task should_accept_a_new_append_at_the_rolled_back_offset()
    {
        // given - a rollback caused by a bad trailer digest
        var chunk1 = Faker.Random.Bytes(700);
        var badChunk = Faker.Random.Bytes(500);
        var retryChunk = Faker.Random.Bytes(500);
        var fileId = await _store.CreateFileAsync(chunk1.Length + retryChunk.Length, metadata: null, AbortToken);

        await using (var body = new MemoryStream(chunk1))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        await using (var body = new MemoryStream(badChunk))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        (await _store.VerifyChecksumAsync(fileId, "sha256", SHA256.HashData(Faker.Random.Bytes(64)), AbortToken))
            .Should()
            .BeFalse();

        // when - the client resumes from the rolled-back offset with different bytes
        await using (var body = new MemoryStream(retryChunk))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", SHA256.HashData(retryChunk), AbortToken);

        // then - the surviving content is chunk1 + the retried chunk, with no residue of the bad one
        verified.Should().BeTrue();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(chunk1.Length + retryChunk.Length);

        var tusFile = await _store.GetFileAsync(fileId, AbortToken);
        await using var contentStream = await tusFile!.GetContentAsync(AbortToken);
        await using var downloaded = new MemoryStream();
        await contentStream.CopyToAsync(downloaded, AbortToken);
        downloaded.ToArray().Should().BeEquivalentTo(chunk1.Concat(retryChunk).ToArray());
    }

    [Fact]
    public async Task verify_throws_when_committed_blocks_do_not_align_with_the_recorded_chunk_offset()
    {
        // given - a committed chunk split across several 256-byte blocks (boundaries at 256/512/700)
        var content = Faker.Random.Bytes(700);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        await using (var body = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // and - the recorded rollback offset is corrupted out-of-band to a value that lands mid-block
        // (300 falls between the 256 and 512 boundaries), simulating drift between the committed Azure
        // block boundaries and tus_last_chunk_offset
        await _CorruptLastChunkOffsetAsync(fileId, offset: 300);

        // when - a faulty-trailer fallback forces a rollback of the last chunk
        var (algorithm, hash) = _GetTrailerFallbackSentinel();
        var act = async () => await _store.VerifyChecksumAsync(fileId, algorithm, hash, AbortToken);

        // then - the guard refuses to guess a rollback point and throws rather than corrupting the blob
        (await act.Should().ThrowAsync<tusdotnet.Models.TusStoreException>())
            .Which.Message.Should()
            .Contain("do not align")
            .And.Contain("300");

        // and - the blob is left untouched (no partial/incorrect rollback committed)
        (await _store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(content.Length);
    }

    /// <summary>
    /// Overwrites <c>tus_last_chunk_offset</c> on the blob with a value that does not fall on a
    /// committed block boundary, preserving every other metadata entry. Simulates the store's tracked
    /// rollback point drifting out of alignment with the committed Azure block list.
    /// </summary>
    private async Task _CorruptLastChunkOffsetAsync(string fileId, long offset)
    {
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);

        var metadata = new Dictionary<string, string>(properties.Value.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            [TusAzureMetadata.LastChunkOffsetKey] = offset.ToInvariantString(),
        };

        await blobClient.SetMetadataAsync(metadata, cancellationToken: AbortToken);
    }

    /// <summary>
    /// Reads tusdotnet's internal faulty-trailer sentinel. <c>ChecksumTrailerHelper.IsFallback</c>
    /// compares by reference, so the exact singleton instances must be passed through.
    /// </summary>
    private static (string Algorithm, byte[] Hash) _GetTrailerFallbackSentinel()
    {
        var field = typeof(ChecksumTrailerHelper).GetField(
            "TrailingChecksumToUseIfRealTrailerIsFaulty",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var sentinel = (tusdotnet.Models.Checksum)field.GetValue(null)!;

        return (sentinel.Algorithm, sentinel.Hash);
    }
}
