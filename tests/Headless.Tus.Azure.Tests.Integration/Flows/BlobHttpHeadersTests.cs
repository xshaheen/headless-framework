// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Testing.Tests;
using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Flows;

/// <summary>
/// Pins that blob HTTP headers set at creation by a custom
/// <see cref="ITusAzureBlobHttpHeadersProvider"/> survive every commit path. Azure's Put Block
/// List <em>clears</em> any <c>x-ms-blob-*</c> property omitted from the request, so a commit that
/// forgets to re-supply the headers silently resets the content type to
/// <c>application/octet-stream</c> — invisible with the default provider, destructive with a
/// custom one.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class BlobHttpHeadersTests : TestBase
{
    private const string _ContainerName = "tusheaders";
    private const string _BlobPrefix = "tusheaders/";
    private const string _ContentType = "video/mp4";
    private const string _CacheControl = "public, max-age=60";

    private readonly TusAzureStore _store;
    private readonly BlobContainerClient _containerClient;

    public BlobHttpHeadersTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        // A small chunk size makes appends span several blocks, exercising the same commit shapes
        // production hits for large uploads.
        var storeOptions = new TusAzureStoreOptions
        {
            ContainerName = _ContainerName,
            BlobPrefix = _BlobPrefix,
            BlobDefaultChunkSize = 256,
        };

        _containerClient = blobServiceClient.GetBlobContainerClient(_ContainerName);
        _store = new TusAzureStore(
            blobServiceClient,
            storeOptions,
            blobHttpHeadersProvider: new CustomHeadersProvider(),
            loggerFactory: LoggerFactory
        );
    }

    [Fact]
    public async Task should_preserve_custom_headers_after_stream_append()
    {
        // given
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when - the plain (non-checksum) stream append commits blocks
        await using (var body = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // then
        await _AssertHeadersPreservedAsync(fileId);
    }

    [Fact]
    public async Task should_preserve_custom_headers_after_pipe_append()
    {
        // given
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(content, AbortToken);
        await pipe.Writer.CompleteAsync();

        // when - the pipeline append commits blocks
        await _store.AppendDataAsync(fileId, pipe.Reader, AbortToken);

        // then
        await _AssertHeadersPreservedAsync(fileId);
    }

    [Fact]
    public async Task should_preserve_custom_headers_after_checksum_verified_commit()
    {
        // given - a checksum-header append stages blocks; VerifyChecksumAsync commits them
        var content = Faker.Random.Bytes(1_000);
        var digest = SHA256.HashData(content);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        await using (var body = ChecksumTestStreams.CreateChecksumAware(content, digest, "sha256"))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // when
        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", digest, AbortToken);

        // then
        verified.Should().BeTrue();
        await _AssertHeadersPreservedAsync(fileId);
    }

    [Fact]
    public async Task should_preserve_custom_headers_after_trailer_rollback()
    {
        // given - a committed chunk whose trailer digest is wrong triggers the rollback re-commit
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        await using (var body = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        // when
        var verified = await _store.VerifyChecksumAsync(
            fileId,
            "sha256",
            SHA256.HashData(Faker.Random.Bytes(64)),
            AbortToken
        );

        // then
        verified.Should().BeFalse();
        await _AssertHeadersPreservedAsync(fileId);
    }

    private async Task _AssertHeadersPreservedAsync(string fileId)
    {
        var blobClient = _containerClient.GetBlobClient(_BlobPrefix + fileId);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: AbortToken);

        properties.Value.ContentType.Should().Be(_ContentType);
        properties.Value.CacheControl.Should().Be(_CacheControl);
    }

    private sealed class CustomHeadersProvider : ITusAzureBlobHttpHeadersProvider
    {
        public ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata)
        {
            return ValueTask.FromResult(
                new BlobHttpHeaders { ContentType = _ContentType, CacheControl = _CacheControl }
            );
        }
    }
}
