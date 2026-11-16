// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using Azure.Storage.Blobs.Specialized;
using Headless.Testing.Tests;
using Headless.Tus;
using Tests.TestSetup;

namespace Tests.RequestCounts;

/// <summary>
/// Pins how many blob requests creation, HEAD, and PATCH cost through the real tusdotnet pipeline, which calls several
/// store methods per request: the store must answer them from one read of the blob's properties and, for an append,
/// one read of its committed block list.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class RequestCountTests(TusAzureFixture fixture) : TestBase
{
    private readonly TusAzureStoreOptions _options = new()
    {
        ContainerName = "tusrequestcounts",
        BlobPrefix = $"{Guid.NewGuid():N}/",
    };

    private TusAzureTestHost _host = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _host = await TusAzureTestHost.StartAsync(fixture, _options, LoggerFactory, AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _host.Dispose();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_create_an_upload_with_a_sliding_expiration_in_two_requests()
    {
        // given
        _host.Counter.Reset();

        // when - creation writes the blob, then tusdotnet sets the sliding expiration on it
        var location = await _host.CreateUploadAsync(100, metadata: null, AbortToken);

        // then - the expiration write reuses the ETag creation returned instead of reading the blob again
        _host.Counter.TotalRequests.Should().Be(2, "counts: {0}", _host.Counter.Describe());
        _host.Counter.Count(BlobRequestCounter.Upload).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.SetMetadata).Should().Be(1);
        (await _host.Store.GetExpirationAsync(TusAzureTestHost.FileIdOf(location), AbortToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_answer_head_with_one_request()
    {
        // given - an upload with committed data
        const string metadata = "filename dGVzdC5iaW4=";
        var location = await _host.CreateUploadAsync(1_000, metadata, AbortToken);

        using (var patch = await _host.PatchAsync(location, Faker.Random.Bytes(400), 0, AbortToken))
        {
            patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        _host.Counter.Reset();

        // when
        using var head = await _host.HeadAsync(location, AbortToken);

        // then - length, offset, metadata, expiration, and concatenation all come from one properties read
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("1000");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("400");
        head.Headers.GetValues("Upload-Metadata").Should().ContainSingle().Which.Should().Be(metadata);
        _host.Counter.TotalRequests.Should().Be(1, "counts: {0}", _host.Counter.Describe());
        _host.Counter.Count(BlobRequestCounter.GetProperties).Should().Be(1);
    }

    [Fact]
    public async Task should_append_a_patch_in_five_requests()
    {
        // given
        var location = await _host.CreateUploadAsync(1_000, metadata: null, AbortToken);
        _host.Counter.Reset();

        // when
        using var patch = await _host.PatchAsync(location, Faker.Random.Bytes(400), 0, AbortToken);

        // then - read the properties and the block list, stage, commit, then refresh the sliding expiration
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patch.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("400");
        _host.Counter.TotalRequests.Should().Be(5, "counts: {0}", _host.Counter.Describe());
        _host.Counter.Count(BlobRequestCounter.GetProperties).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.GetBlockList).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.StageBlock).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.CommitBlockList).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.SetMetadata).Should().Be(1);
    }

    [Fact]
    public async Task should_append_a_checksum_patch_in_six_requests()
    {
        // given
        var content = Faker.Random.Bytes(400);
        var location = await _host.CreateUploadAsync(1_000, metadata: null, AbortToken);
        _host.Counter.Reset();

        // when
        using var patch = await _host.PatchAsync(location, content, 0, AbortToken, SHA256.HashData(content));

        // then - read once, stage and record the pending chunk, commit it once verified, refresh the expiration
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patch.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("400");
        _host.Counter.TotalRequests.Should().Be(6, "counts: {0}", _host.Counter.Describe());
        _host.Counter.Count(BlobRequestCounter.GetProperties).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.GetBlockList).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.StageBlock).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.CommitBlockList).Should().Be(1);
        _host.Counter.Count(BlobRequestCounter.SetMetadata).Should().Be(2);
    }

    [Fact]
    public async Task should_report_the_committed_offset_while_a_checksum_chunk_waits_staged()
    {
        // given - a block staged but never committed, as a checksum PATCH leaves it before verification
        var location = await _host.CreateUploadAsync(1_000, metadata: null, AbortToken);

        using (var first = await _host.PatchAsync(location, Faker.Random.Bytes(300), 0, AbortToken))
        {
            first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var blockBlob = _host
            .BlobServiceClient.GetBlobContainerClient(_options.ContainerName)
            .GetBlockBlobClient(_options.BlobPrefix + TusAzureTestHost.FileIdOf(location));
        await using (var staged = new MemoryStream(Faker.Random.Bytes(200)))
        {
            await blockBlob.StageBlockAsync(
                Convert.ToBase64String("block-staged00-0000000001"u8.ToArray()),
                staged,
                cancellationToken: AbortToken
            );
        }

        // when
        using var head = await _host.HeadAsync(location, AbortToken);

        // then - the offset counts committed bytes only
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("300");
    }

    [Fact]
    public async Task should_read_the_blob_again_in_a_new_request()
    {
        // given
        var location = await _host.CreateUploadAsync(1_000, metadata: null, AbortToken);
        using var first = await _host.HeadAsync(location, AbortToken);
        first.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");

        // when - another writer appends between two requests
        await using (var body = new MemoryStream(Faker.Random.Bytes(42)))
        {
            await _host.Store.AppendDataAsync(TusAzureTestHost.FileIdOf(location), body, AbortToken);
        }

        using var head = await _host.HeadAsync(location, AbortToken);

        // then - the memo lives for one request only
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("42");
    }
}
