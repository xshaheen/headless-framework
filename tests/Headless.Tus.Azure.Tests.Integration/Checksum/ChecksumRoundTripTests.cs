// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus.Options;
using Headless.Tus.Services;
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

    /// <summary>
    /// Wraps <paramref name="content"/> in tusdotnet's internal <c>ChecksumAwareStream</c> (the only thing
    /// the store's <c>GetUploadChecksumInfo()</c> recognizes) so <c>AppendDataAsync</c> runs the hashing
    /// path. The type is internal to tusdotnet, hence reflection; only its <c>Algorithm</c> is read here.
    /// </summary>
    private static Stream _CreateChecksumAwareStream(byte[] content, byte[] digest)
    {
        var checksum = new tusdotnet.Models.Checksum($"sha256 {Convert.ToBase64String(digest)}");
        var streamType = typeof(tusdotnet.Models.Checksum).Assembly.GetType(
            "tusdotnet.Models.Streams.ChecksumAwareStream",
            throwOnError: true
        )!;

        return (Stream)Activator.CreateInstance(streamType, new MemoryStream(content), checksum)!;
    }
}
