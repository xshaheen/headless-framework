// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Tests.TestSetup;

namespace Tests.Checksum;

/// <summary>
/// A chunk sent with an <c>Upload-Checksum</c> trailer commits before its digest arrives. These tests run each store
/// call under the <see cref="HttpContext" /> of the tus request that makes it, the way tusdotnet does, and pin that the
/// chunk counts toward the offset, the readable content, and completion only once its verification succeeds: a
/// verification that throws or never runs leaves the upload resumable from the chunk's start.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class ChecksumTrailerVerifiedOffsetTests(TusAzureFixture fixture) : TestBase
{
    private const string _Sha256 = "sha256";

    private readonly HttpContextAccessor _accessor = new();
    private readonly BlobRequestFault _fault = new();
    private TusAzureStore _store = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var clientOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04);
        clientOptions.AddPolicy(_fault, HttpPipelinePosition.PerCall);
        var client = new BlobServiceClient(fixture.Container.GetConnectionString(), clientOptions);

        // A small chunk size splits each chunk into several blocks, so a rollback has to cut the block list at the
        // chunk's start rather than drop one block.
        var options = new TusAzureStoreOptions
        {
            ContainerName = "tusverifiedoffset",
            BlobPrefix = $"{Guid.NewGuid():N}/",
            BlobDefaultChunkSize = 256,
        };
        await new TusAzureContainerProvisioner(client, options).ProvisionAsync(AbortToken);

        _store = new TusAzureStore(client, options, loggerFactory: LoggerFactory, httpContextAccessor: _accessor);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _fault.Disarm();
        _accessor.HttpContext = null;

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_report_the_pre_chunk_offset_when_the_verification_download_fails()
    {
        // given - a verified first chunk, then a trailer chunk whose verification cannot read the committed range
        var chunk1 = Faker.Random.Bytes(700);
        var chunk2 = Faker.Random.Bytes(500);
        var fileId = await _store.CreateFileAsync(chunk1.Length + chunk2.Length, metadata: null, AbortToken);
        await _PatchWithTrailerAsync(fileId, chunk1);

        _BeginTrailerRequest(SHA256.HashData(chunk2));
        await _AppendAsync(fileId, chunk2);
        _fault.Arm(BlobRequestFault.RangedDownload(fileId));

        // when
        var verify = () => _store.VerifyChecksumAsync(fileId, _Sha256, SHA256.HashData(chunk2), AbortToken);

        // then - the failure propagates, and the next request sees the upload as it was before the chunk
        await verify.Should().ThrowAsync<RequestFailedException>();
        _fault.Disarm();

        _BeginPlainRequest();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(chunk1.Length);
        (await _ReadContentAsync(fileId)).Should().Equal(chunk1);
    }

    [Fact]
    public async Task should_accept_a_retry_at_the_verified_offset_and_keep_only_the_retried_bytes()
    {
        // given - a trailer chunk whose verification failed on a storage error
        var chunk1 = Faker.Random.Bytes(700);
        var chunk2 = Faker.Random.Bytes(500);
        var fileId = await _store.CreateFileAsync(chunk1.Length + chunk2.Length, metadata: null, AbortToken);
        await _PatchWithTrailerAsync(fileId, chunk1);

        _BeginTrailerRequest(SHA256.HashData(chunk2));
        await _AppendAsync(fileId, chunk2);
        _fault.Arm(BlobRequestFault.RangedDownload(fileId));
        var failed = () => _store.VerifyChecksumAsync(fileId, _Sha256, SHA256.HashData(chunk2), AbortToken);
        await failed.Should().ThrowAsync<RequestFailedException>();
        _fault.Disarm();

        // when - the client resumes from the offset HEAD reports and resends the chunk
        _BeginPlainRequest();
        var resumeAt = await _store.GetUploadOffsetAsync(fileId, AbortToken);
        var verified = await _PatchWithTrailerAsync(fileId, chunk2);

        // then - the upload holds exactly what the client sent, once
        resumeAt.Should().Be(chunk1.Length);
        verified.Should().BeTrue();

        _BeginPlainRequest();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(chunk1.Length + chunk2.Length);
        (await _ReadContentAsync(fileId)).Should().Equal([.. chunk1, .. chunk2]);
    }

    [Fact]
    public async Task should_report_the_pre_chunk_offset_when_the_process_stops_before_verification()
    {
        // given - a trailer chunk appended through the pipe, after which the process stopped before verifying it
        var content = Faker.Random.Bytes(900);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        await _store.SetExpirationAsync(fileId, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero), AbortToken);

        _BeginTrailerRequest(SHA256.HashData(content));
        await _store.AppendDataAsync(fileId, PipeReader.Create(new MemoryStream(content)), AbortToken);

        // when - a later request reads the upload
        _BeginPlainRequest();

        // then - nothing counts: the upload is incomplete, still expires, and holds no readable bytes
        (await _store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(0);
        (await _store.GetExpirationAsync(fileId, AbortToken)).Should().NotBeNull();
        (await _ReadContentAsync(fileId)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_the_pre_chunk_offset_when_a_mismatch_fails_to_roll_back()
    {
        // given - a verified first chunk, then a trailer chunk whose digest does not match
        var chunk1 = Faker.Random.Bytes(600);
        var chunk2 = Faker.Random.Bytes(400);
        var fileId = await _store.CreateFileAsync(chunk1.Length + chunk2.Length, metadata: null, AbortToken);
        await _PatchWithTrailerAsync(fileId, chunk1);

        var wrongDigest = SHA256.HashData(Faker.Random.Bytes(400));
        _BeginTrailerRequest(wrongDigest);
        await _AppendAsync(fileId, chunk2);

        // when - the rollback's block-list commit fails
        _fault.Arm(BlobRequestFault.BlockListCommit(fileId));
        var verify = () => _store.VerifyChecksumAsync(fileId, _Sha256, wrongDigest, AbortToken);

        // then - the chunk still does not count
        await verify.Should().ThrowAsync<RequestFailedException>();
        _fault.Disarm();

        _BeginPlainRequest();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(chunk1.Length);
    }

    [Fact]
    public async Task should_count_a_chunk_once_its_trailer_digest_matches()
    {
        // given
        var content = Faker.Random.Bytes(800);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when
        var verified = await _PatchWithTrailerAsync(fileId, content);

        // then - the upload is finished: full offset, no expiry, every byte readable
        verified.Should().BeTrue();
        _BeginPlainRequest();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
        (await _store.GetExpirationAsync(fileId, AbortToken)).Should().BeNull();
        (await _ReadContentAsync(fileId)).Should().Equal(content);
    }

    [Fact]
    public async Task should_count_a_chunk_whose_declared_trailer_never_arrives()
    {
        // given - a request that declares the trailer but sends it empty and stays connected; tusdotnet never
        // verifies such a chunk, so it must count at once or a finished upload would read short forever
        var content = Faker.Random.Bytes(500);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        _BeginTrailerRequest(digest: null);

        // when
        await _AppendAsync(fileId, content);

        // then
        _BeginPlainRequest();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    [Fact]
    public async Task should_report_the_full_offset_after_each_patch_without_a_checksum()
    {
        // given
        var chunks = new[] { Faker.Random.Bytes(300), Faker.Random.Bytes(450), Faker.Random.Bytes(250) };
        var fileId = await _store.CreateFileAsync(chunks.Sum(chunk => chunk.Length), metadata: null, AbortToken);
        var expectedOffset = 0L;

        foreach (var chunk in chunks)
        {
            // when
            _BeginPlainRequest();
            await _AppendAsync(fileId, chunk);
            expectedOffset += chunk.Length;

            // then
            _BeginPlainRequest();
            (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(expectedOffset);
        }

        (await _store.GetExpirationAsync(fileId, AbortToken)).Should().BeNull();
        (await _ReadContentAsync(fileId)).Should().Equal(chunks.SelectMany(chunk => chunk).ToArray());
    }

    /// <summary>One PATCH whose digest arrives as a trailer: the append, then the verification tusdotnet runs.</summary>
    private async Task<bool> _PatchWithTrailerAsync(string fileId, byte[] content)
    {
        var digest = SHA256.HashData(content);
        _BeginTrailerRequest(digest);
        await _AppendAsync(fileId, content);

        return await _store.VerifyChecksumAsync(fileId, _Sha256, digest, AbortToken);
    }

    private async Task _AppendAsync(string fileId, byte[] content)
    {
        await using var body = new MemoryStream(content);
        await _store.AppendDataAsync(fileId, body, AbortToken);
    }

    private async Task<byte[]> _ReadContentAsync(string fileId)
    {
        var file = await _store.GetFileAsync(fileId, AbortToken);
        await using var content = await file!.GetContentAsync(AbortToken);
        await using var copy = new MemoryStream();
        await content.CopyToAsync(copy, AbortToken);

        return copy.ToArray();
    }

    /// <summary>Starts a request that carries no trailer, with nothing kept from an earlier request.</summary>
    private void _BeginPlainRequest()
    {
        _accessor.HttpContext = new DefaultHttpContext();
    }

    /// <summary>
    /// Starts a request that declares an <c>Upload-Checksum</c> trailer and, after its body, delivers
    /// <paramref name="digest"/> in it, as an HTTP/2 or chunked HTTP/1.1 tus client does; a <see langword="null"/>
    /// digest delivers the trailers without that one.
    /// </summary>
    private void _BeginTrailerRequest(byte[]? digest)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Trailer"] = "Upload-Checksum";

        var trailers = new HeaderDictionary();

        if (digest is not null)
        {
            trailers["Upload-Checksum"] = $"{_Sha256} {Convert.ToBase64String(digest)}";
        }

        context.Features.Set<IHttpRequestTrailersFeature>(new ArrivedTrailers(trailers));
        _accessor.HttpContext = context;
    }

    private sealed class ArrivedTrailers(IHeaderDictionary trailers) : IHttpRequestTrailersFeature
    {
        public bool Available => true;

        public IHeaderDictionary Trailers => trailers;
    }
}
