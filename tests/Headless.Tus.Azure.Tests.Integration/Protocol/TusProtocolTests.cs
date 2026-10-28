// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests.Protocol;

/// <summary>
/// Runs the shared tus protocol scenarios against the Azure store, plus the HTTP cases that depend on Azure-only
/// behavior: 460 on checksum mismatch and the chunk-splitting options, whose effect shows in the committed block list.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class TusProtocolTests(TusAzureFixture fixture) : TusProtocolConformanceTestsBase<TusAzureStore>(fixture)
{
    [Fact]
    public async Task patch_with_mismatching_checksum_responds_460_and_discards_the_chunk()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var content = Faker.Random.Bytes(128);
        var location = await CreateUploadAsync(client, content.Length, metadata: null);

        // when - Upload-Checksum carries a digest of DIFFERENT bytes
        var wrongDigest = SHA256.HashData(Faker.Random.Bytes(128));
        using var patched = await PatchAsync(client, location, content, offset: 0, checksum: wrongDigest);

        // then - 460 (checksum mismatch) and the chunk was not applied
        ((int)patched.StatusCode)
            .Should()
            .Be(460);

        using var head = await HeadAsync(client, location);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");

        // and a clean retry at the same offset completes the upload
        var digest = SHA256.HashData(content);
        using var retried = await PatchAsync(client, location, content, offset: 0, checksum: digest);
        retried.StatusCode.Should().Be(HttpStatusCode.NoContent);
        retried.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("128");
    }

    [Fact]
    public async Task patch_with_chunk_splitting_disabled_commits_a_single_block()
    {
        // given - chunk splitting OFF with a deliberately small block size, so a body spanning several
        // BlobDefaultChunkSize windows would become multiple Azure blocks if the option were ignored on
        // the HTTP pipeline path (the regression this pins: EnableChunkSplitting used to be honored only
        // by the store-direct Stream overload, never by the PipeReader path every real PATCH uses)
        using var host = await _CreateHostAsync(enableChunkSplitting: false, blobDefaultChunkSize: 256);
        using var client = host.GetTestClient();

        var content = Faker.Random.Bytes(1024); // 4x the 256-byte chunk window
        var location = await CreateUploadAsync(client, content.Length, metadata: null);

        // when - a single PATCH delivers the whole body through the real MapTus pipeline
        using var patched = await PatchAsync(client, location, content, offset: 0);

        // then - the upload completes and the store committed exactly ONE block (no splitting)
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patched.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("1024");
        (await _GetCommittedBlockCountAsync(location)).Should().Be(1);
    }

    [Fact]
    public async Task patch_with_chunk_splitting_disabled_and_matching_checksum_commits_a_single_block()
    {
        // given - chunk splitting OFF plus an Upload-Checksum header: the no-split path stages the whole
        // body as one deferred block and hashes it in a single pass, then VerifyChecksumAsync commits it
        using var host = await _CreateHostAsync(enableChunkSplitting: false, blobDefaultChunkSize: 256);
        using var client = host.GetTestClient();

        var content = Faker.Random.Bytes(1024); // spans multiple 256-byte windows, yet stays one block
        var location = await CreateUploadAsync(client, content.Length, metadata: null);

        // when - the PATCH carries the correct digest of the whole body
        using var patched = await PatchAsync(client, location, content, offset: 0, checksum: SHA256.HashData(content));

        // then - the chunk is accepted and committed as exactly one block
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patched.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("1024");
        (await _GetCommittedBlockCountAsync(location)).Should().Be(1);
    }

    [Fact]
    public async Task patch_with_chunk_splitting_disabled_and_mismatching_checksum_discards_the_chunk()
    {
        // given - chunk splitting OFF with a digest that does not match the body
        using var host = await _CreateHostAsync(enableChunkSplitting: false, blobDefaultChunkSize: 256);
        using var client = host.GetTestClient();

        var content = Faker.Random.Bytes(1024);
        var location = await CreateUploadAsync(client, content.Length, metadata: null);

        // when - the single staged block fails verification
        var wrongDigest = SHA256.HashData(Faker.Random.Bytes(1024));
        using var patched = await PatchAsync(client, location, content, offset: 0, checksum: wrongDigest);

        // then - 460 (checksum mismatch); the deferred block is left uncommitted, so the offset stays 0
        ((int)patched.StatusCode)
            .Should()
            .Be(460);
        (await _GetCommittedBlockCountAsync(location)).Should().Be(0);

        using var head = await HeadAsync(client, location);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public async Task patch_with_chunk_splitting_disabled_rejects_declared_over_length_body_at_the_protocol_layer()
    {
        // given - chunk splitting OFF and a declared Upload-Length smaller than the PATCH body
        using var host = await _CreateHostAsync(enableChunkSplitting: false, blobDefaultChunkSize: 256);
        using var client = host.GetTestClient();

        var location = await CreateUploadAsync(client, uploadLength: 512, metadata: null);

        // when - a single PATCH delivers more than the declared length
        using var patched = await PatchAsync(client, location, Faker.Random.Bytes(1024), offset: 0);

        // then - tusdotnet rejects it up front (413, Upload-Offset + body > Upload-Length) before the
        // store buffers anything, and nothing is committed
        ((int)patched.StatusCode)
            .Should()
            .Be(413);
        (await _GetCommittedBlockCountAsync(location)).Should().Be(0);

        using var head = await HeadAsync(client, location);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public async Task patch_with_chunk_splitting_disabled_rejects_deferred_length_body_exceeding_the_buffer_cap()
    {
        // given - chunk splitting OFF with a tiny no-split buffer cap. A deferred-length upload has no
        // declared length, so tusdotnet cannot pre-reject the PATCH by size (as the over-length case
        // above shows) and the body reaches the store's PipeReader path — where MaxNoSplitBufferSize is
        // the only in-flight memory bound (#5). This pins that the cap is enforced on the real pipeline.
        using var host = await _CreateHostAsync(enableChunkSplitting: false, maxNoSplitBufferSize: 4096);
        using var client = host.GetTestClient();

        // deferred-length create (no Upload-Length)
        using var create = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        create.Headers.Add("Tus-Resumable", "1.0.0");
        create.Headers.Add("Upload-Defer-Length", "1");
        using var created = await client.SendAsync(create, AbortToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = created.Headers.Location!.ToString();

        // when - a single PATCH (still deferred: no Upload-Length) delivers more than the cap
        using var patched = await PatchAsync(client, location, Faker.Random.Bytes(8192), offset: 0);

        // then - the store rejects it (400) with nothing committed and the offset unchanged
        ((int)patched.StatusCode)
            .Should()
            .Be(400);
        (await _GetCommittedBlockCountAsync(location)).Should().Be(0);

        using var head = await HeadAsync(client, location);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    private async Task<int> _GetCommittedBlockCountAsync(string location)
    {
        var fileId = location[(location.LastIndexOf('/') + 1)..];

        var blockBlobClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        )
            .GetBlobContainerClient("tusprotocol")
            .GetBlockBlobClient($"tusprotocol/{fileId}");

        var blockList = await blockBlobClient.GetBlockListAsync(
            BlockListTypes.Committed,
            cancellationToken: AbortToken
        );

        return blockList.Value.CommittedBlocks.Count();
    }

    private async Task<IHost> _CreateHostAsync(
        bool enableChunkSplitting = true,
        int? blobDefaultChunkSize = null,
        int? maxNoSplitBufferSize = null
    )
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions
        {
            ContainerName = "tusprotocol",
            BlobPrefix = "tusprotocol/",
            EnableChunkSplitting = enableChunkSplitting,
        };

        if (blobDefaultChunkSize is not null)
        {
            storeOptions.BlobDefaultChunkSize = blobDefaultChunkSize.Value;
        }

        if (maxNoSplitBufferSize is not null)
        {
            storeOptions.MaxNoSplitBufferSize = maxNoSplitBufferSize.Value;
        }

        await new TusAzureContainerProvisioner(blobServiceClient, storeOptions).ProvisionAsync(AbortToken);
        var store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);

        return await StartHostAsync(store);
    }

    [Fact]
    public override Task head_echoes_metadata_verbatim_and_reports_length_and_offset()
    {
        return base.head_echoes_metadata_verbatim_and_reports_length_and_offset();
    }

    [Fact]
    public override Task defer_length_upload_reports_defer_header_until_length_is_declared()
    {
        return base.defer_length_upload_reports_defer_header_until_length_is_declared();
    }

    [Fact]
    public override Task patch_advances_offset_and_reports_expiration()
    {
        return base.patch_advances_offset_and_reports_expiration();
    }

    [Fact]
    public override Task patch_with_wrong_offset_conflicts_without_modifying_the_upload()
    {
        return base.patch_with_wrong_offset_conflicts_without_modifying_the_upload();
    }

    [Fact]
    public override Task delete_terminates_the_upload()
    {
        return base.delete_terminates_the_upload();
    }
}
