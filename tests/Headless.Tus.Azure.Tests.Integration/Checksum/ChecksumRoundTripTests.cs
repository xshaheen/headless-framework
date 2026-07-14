// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Checksum;

/// <summary>
/// Exercises the real checksum hashing path (<c>AppendDataAsync</c> with an Upload-Checksum, then
/// <c>VerifyChecksumAsync</c>), which the rest of the suite skips. This proves the store's incremental
/// hash, fed chunk-by-chunk, equals a one-shot SHA-256 of the same bytes — i.e. the digest a real TUS
/// client computes. A small <see cref="TusAzureStoreOptions.BlobDefaultChunkSize"/> forces the body to
/// span many blocks so the per-chunk accumulation is genuinely tested.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class ChecksumRoundTripTests : TestBase
{
    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;
    private const string _ContainerName = "tuschecksum";
    private const string _BlobPrefix = "tuschecksum/";

    public ChecksumRoundTripTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        // 256-byte default chunk forces a multi-KB body to split into many blocks, exercising the
        // chunk-by-chunk IncrementalHash.AppendData accumulation rather than a single AppendData call.
        var storeOptions = new TusAzureStoreOptions
        {
            ContainerName = _ContainerName,
            BlobPrefix = _BlobPrefix,
            BlobDefaultChunkSize = 256,
        };

        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    [Fact]
    public async Task verify_commits_when_streamed_sha256_matches_the_client_digest()
    {
        // given - several KB so the body splits across ~16 blocks
        var content = Faker.Random.Bytes(4_000);
        var expectedDigest = SHA256.HashData(content);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when - append with a sha256 Upload-Checksum (blocks staged but not committed), then verify
        await using var body = _CreateChecksumAwareStream(content, expectedDigest);
        await _store.AppendDataAsync(fileId, body, AbortToken);

        // nothing is committed until verification passes
        (await _store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(0);

        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", expectedDigest, AbortToken);

        // then - the store's incremental digest matched the client's, so the data is committed
        verified.Should().BeTrue();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);

        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloaded = new MemoryStream();
        await blobClient.DownloadToAsync(downloaded, AbortToken);
        downloaded.ToArray().Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task verify_leaves_data_uncommitted_when_the_digest_does_not_match()
    {
        // given
        var content = Faker.Random.Bytes(4_000);
        var realDigest = SHA256.HashData(content);
        var wrongDigest = SHA256.HashData(Faker.Random.Bytes(4_000));
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when - the staged digest is the real one, but the client claims a different digest
        await using var body = _CreateChecksumAwareStream(content, realDigest);
        await _store.AppendDataAsync(fileId, body, AbortToken);

        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", wrongDigest, AbortToken);

        // then - mismatch leaves the staged blocks uncommitted (Azure GC discards them)
        verified.Should().BeFalse();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_accept_a_new_append_at_the_old_offset_after_a_failed_verification()
    {
        // given - a 460 flow: staged chunk discarded because the digest did not match
        var content = Faker.Random.Bytes(4_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        await using (var body = _CreateChecksumAwareStream(content, SHA256.HashData(content)))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        (await _store.VerifyChecksumAsync(fileId, "sha256", SHA256.HashData(Faker.Random.Bytes(64)), AbortToken))
            .Should()
            .BeFalse();

        // when - the client retries the chunk at the unchanged offset, this time verifying clean
        var retryContent = Faker.Random.Bytes(4_000);
        var retryDigest = SHA256.HashData(retryContent);

        await using (var body = _CreateChecksumAwareStream(retryContent, retryDigest))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", retryDigest, AbortToken);

        // then - the retried chunk (not the discarded one) is the committed content
        verified.Should().BeTrue();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(retryContent.Length);

        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloaded = new MemoryStream();
        await blobClient.DownloadToAsync(downloaded, AbortToken);
        downloaded.ToArray().Should().BeEquivalentTo(retryContent);
    }

    [Fact]
    public async Task should_commit_a_chunk_spanning_hundreds_of_staged_blocks()
    {
        // given - 60 KB at a 256-byte chunk size stages ~235 blocks in one append. The staged-block
        // tracking must stay constant-size in blob metadata (Azure caps total metadata at 8 KB, and
        // a comma-joined ID list would exceed it at ~211 blocks and wedge the upload).
        var content = Faker.Random.Bytes(60_000);
        var digest = SHA256.HashData(content);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when
        await using (var body = _CreateChecksumAwareStream(content, digest))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", digest, AbortToken);

        // then - all staged blocks are reconstructed and committed in order
        verified.Should().BeTrue();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);

        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        await using var downloaded = new MemoryStream();
        await blobClient.DownloadToAsync(downloaded, AbortToken);
        downloaded.ToArray().Should().BeEquivalentTo(content);
    }

    private static Stream _CreateChecksumAwareStream(byte[] content, byte[] digest)
    {
        return ChecksumTestStreams.CreateChecksumAware(content, digest, "sha256");
    }
}
