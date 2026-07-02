// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Tests.TestSetup;
using tusdotnet.Helpers;

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
