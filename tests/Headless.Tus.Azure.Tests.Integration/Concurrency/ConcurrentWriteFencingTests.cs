// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Tests.Concurrency;

/// <summary>
/// Two requests for one upload that both believe they hold the file lock (the lock was lost
/// without the holder noticing). Each test lets both requests read the blob, holds them at one
/// Azure write, then releases them one at a time so the second runs entirely after the first has
/// committed: its view of the blob is stale, so its write must be rejected instead of overwriting
/// the first request's committed state. (Holding both at the commit instead would not exercise the
/// fence: Put Block List discards the uncommitted blocks it does not name, so the loser's commit
/// would fail on its own missing blocks.)
/// </summary>
[Collection<TusAzureFixture>]
public sealed class ConcurrentWriteFencingTests : TestBase
{
    private const string _ContainerName = "tusfencing";
    private const string _Endpoint = "/files";

    private readonly BlobWriteGate _stageGate = new(RequestMethod.Put, "block");
    private readonly BlobWriteGate _metadataGate = new(RequestMethod.Put, "metadata");
    private readonly BlobWriteGate _deleteGate = new(RequestMethod.Delete, comp: null);
    private readonly TusAzureStore _store;

    public ConcurrentWriteFencingTests(TusAzureFixture fixture)
    {
        var clientOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04);
        clientOptions.AddPolicy(_stageGate, HttpPipelinePosition.PerCall);
        clientOptions.AddPolicy(_metadataGate, HttpPipelinePosition.PerCall);
        clientOptions.AddPolicy(_deleteGate, HttpPipelinePosition.PerCall);

        var blobServiceClient = new BlobServiceClient(fixture.Container.GetConnectionString(), clientOptions);
        // A prefix per test keeps the reaper test's scan to its own uploads, even on a reused container.
        var storeOptions = new TusAzureStoreOptions
        {
            ContainerName = _ContainerName,
            BlobPrefix = $"{Guid.NewGuid():N}/",
        };

        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _stageGate.Dispose();
        _metadataGate.Dispose();
        _deleteGate.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_keep_an_expired_upload_when_it_changes_between_the_reaper_finding_and_deleting_it()
    {
        // given - an incomplete upload whose expiration has passed
        var fileId = await _store.CreateFileAsync(1_000, metadata: null, AbortToken);
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddMinutes(-10), AbortToken);
        _deleteGate.Arm(fileId);

        // when - the reaper finds it expired, but a resume refreshes the expiration before the delete
        var removal = _store.RemoveExpiredFilesAsync(AbortToken);
        await _deleteGate.WaitForArrivalAsync(AbortToken);
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);
        _deleteGate.ReleaseOne();
        var removed = await removal;
        _deleteGate.Disarm();

        // then - the delete was conditional on the state the reaper saw, so the upload survives
        removed.Should().Be(0);
        (await _store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_the_stale_commit_when_two_appends_race_on_one_upload()
    {
        // given - two appends of different sizes that both read the empty upload before either commits
        var first = Faker.Random.Bytes(300);
        var second = Faker.Random.Bytes(500);
        var fileId = await _store.CreateFileAsync(first.Length + second.Length, metadata: null, AbortToken);
        _stageGate.Arm(fileId);

        var firstAppend = _AppendAsync(fileId, first);
        var secondAppend = _AppendAsync(fileId, second);
        await _stageGate.WaitForArrivalAsync(AbortToken);
        await _stageGate.WaitForArrivalAsync(AbortToken);

        // when - one append stages and commits; only then does the other stage its block and commit
        _stageGate.ReleaseOne();
        var winner = await Task.WhenAny(firstAppend, secondAppend);
        var loser = winner == firstAppend ? secondAppend : firstAppend;
        var winnerBytes = await winner;

        _stageGate.ReleaseOne();
        var loserAct = async () => await loser;

        // then - the stale commit fails with the Azure precondition error (not a TusStoreException,
        // which tusdotnet would answer with a non-retryable 400)
        var rejection = await loserAct.Should().ThrowExactlyAsync<RequestFailedException>();
        rejection.Which.Status.Should().Be(412);
        _stageGate.Disarm();

        // and the committed blob is exactly the winner's append
        var winnerContent = winner == firstAppend ? first : second;
        winnerBytes.Should().Be(winnerContent.Length);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(winnerBytes);
        (await _DownloadAsync(fileId)).Should().Equal(winnerContent);
    }

    [Fact]
    public async Task should_reject_the_stale_checksum_staging_when_two_appends_race_on_one_upload()
    {
        // given - two checksum-header appends that both read the upload before either records its
        // staged blocks in the blob metadata
        var first = Faker.Random.Bytes(300);
        var second = Faker.Random.Bytes(500);
        var fileId = await _store.CreateFileAsync(first.Length + second.Length, metadata: null, AbortToken);
        _metadataGate.Arm(fileId);

        var firstAppend = _AppendWithChecksumAsync(fileId, first);
        var secondAppend = _AppendWithChecksumAsync(fileId, second);
        await _metadataGate.WaitForArrivalAsync(AbortToken);
        await _metadataGate.WaitForArrivalAsync(AbortToken);

        // when
        _metadataGate.ReleaseOne();
        var winner = await Task.WhenAny(firstAppend, secondAppend);
        var loser = winner == firstAppend ? secondAppend : firstAppend;
        await winner;

        _metadataGate.ReleaseOne();
        var loserAct = async () => await loser;

        // then - the stale staging is rejected, so it cannot replace the winner's pending chunk
        var rejection = await loserAct.Should().ThrowExactlyAsync<RequestFailedException>();
        rejection.Which.Status.Should().Be(412);
        _metadataGate.Disarm();

        // and the winner's chunk still verifies and commits
        var winnerContent = winner == firstAppend ? first : second;
        var verified = await _store.VerifyChecksumAsync(fileId, "sha256", SHA256.HashData(winnerContent), AbortToken);

        verified.Should().BeTrue();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(winnerContent.Length);
        (await _DownloadAsync(fileId)).Should().Equal(winnerContent);
    }

    [Fact]
    public async Task should_complete_every_write_of_a_patch_when_no_other_request_writes_the_upload()
    {
        // given - the store calls one checksum PATCH makes, in tusdotnet's order: stage + record the
        // pending chunk, refresh the sliding expiration, then verify + commit
        var chunk1 = Faker.Random.Bytes(400);
        var chunk2 = Faker.Random.Bytes(300);
        var fileId = await _store.CreateFileAsync(chunk1.Length + chunk2.Length, metadata: null, AbortToken);

        // when
        await _AppendWithChecksumAsync(fileId, chunk1);
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);
        var firstVerified = await _store.VerifyChecksumAsync(fileId, "sha256", SHA256.HashData(chunk1), AbortToken);

        // and a trailer-style PATCH (commit during the append) whose digest is wrong, so verification
        // rolls the chunk back with one more commit
        await _AppendAsync(fileId, chunk2);
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);
        var secondVerified = await _store.VerifyChecksumAsync(
            fileId,
            "sha256",
            SHA256.HashData(Faker.Random.Bytes(300)),
            AbortToken
        );

        // then - every write of each request matched the blob it had just read or written
        firstVerified.Should().BeTrue();
        secondVerified.Should().BeFalse();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(chunk1.Length);
        (await _DownloadAsync(fileId)).Should().Equal(chunk1);
    }

    [Fact]
    public async Task should_answer_the_stale_patch_with_a_retryable_error_and_no_offset_when_two_patches_race()
    {
        // given - a tus endpoint whose file lock always grants, standing in for a lock that was lost
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var first = Faker.Random.Bytes(300);
        var second = Faker.Random.Bytes(500);
        var location = await _CreateUploadAsync(client, first.Length + second.Length);
        var fileId = location[(location.LastIndexOf('/') + 1)..];
        _stageGate.Arm(fileId);

#pragma warning disable CA2025 // False positive: both tasks are awaited below, before client goes out of scope.
        var firstPatch = _PatchAsync(client, location, first);
        var secondPatch = _PatchAsync(client, location, second);
#pragma warning restore CA2025
        await _stageGate.WaitForArrivalAsync(AbortToken);
        await _stageGate.WaitForArrivalAsync(AbortToken);

        // when - one PATCH stages and commits before the other is let go to stage its block
        _stageGate.ReleaseOne();
        var firstFinished = await Task.WhenAny(firstPatch, secondPatch);
        _stageGate.ReleaseOne();

        using var firstResponse = await firstPatch;
        using var secondResponse = await secondPatch;
        _stageGate.Disarm();

        // then - the winner reports its offset; the loser gets a server error (tus clients retry it
        // by HEAD + resume) and never a success offset for bytes that did not land
        var firstWon = firstFinished == firstPatch;
        var (winnerResponse, loserResponse) = firstWon
            ? (firstResponse, secondResponse)
            : (secondResponse, firstResponse);
        var winnerContent = firstWon ? first : second;
        winnerResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        winnerResponse
            .Headers.GetValues("Upload-Offset")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(winnerContent.Length.ToString(CultureInfo.InvariantCulture));

        loserResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        loserResponse.Headers.Contains("Upload-Offset").Should().BeFalse();

        // and HEAD reports the winner's offset for the client to resume from
        using var head = new HttpRequestMessage(HttpMethod.Head, location);
        head.Headers.Add("Tus-Resumable", "1.0.0");
        using var headResponse = await client.SendAsync(head, AbortToken);
        headResponse
            .Headers.GetValues("Upload-Offset")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(winnerContent.Length.ToString(CultureInfo.InvariantCulture));
        (await _DownloadAsync(fileId)).Should().Equal(winnerContent);
    }

    private async Task<long> _AppendAsync(string fileId, byte[] content)
    {
        await using var body = new MemoryStream(content);

        return await _store.AppendDataAsync(fileId, body, AbortToken);
    }

    private async Task<long> _AppendWithChecksumAsync(string fileId, byte[] content)
    {
        await using var body = ChecksumTestStreams.CreateChecksumAware(content, SHA256.HashData(content), "sha256");

        return await _store.AppendDataAsync(fileId, body, AbortToken);
    }

    private async Task<byte[]> _DownloadAsync(string fileId)
    {
        var tusFile = await _store.GetFileAsync(fileId, AbortToken);
        await using var content = await tusFile!.GetContentAsync(AbortToken);
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, AbortToken);

        return buffer.ToArray();
    }

    private async Task<IHost> _CreateHostAsync()
    {
        var configuration = new DefaultTusConfiguration
        {
            Store = _store,
            FileLockProvider = new AlwaysGrantedFileLockProvider(),
        };

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    // Kestrel answers an unhandled exception that escapes before the response started
                    // with 500; TestServer would rethrow it into the client instead, so mirror Kestrel.
                    app.Use(
                        async (context, next) =>
                        {
                            try
                            {
                                await next(context);
                            }
                            catch (RequestFailedException) when (!context.Response.HasStarted)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            }
                        }
                    );
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapTus(_Endpoint, _ => Task.FromResult(configuration)));
                })
        );

        return await builder.StartAsync(AbortToken);
    }

    private static async Task<string> _CreateUploadAsync(HttpClient client, int uploadLength)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _Endpoint);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Length", uploadLength.ToString(CultureInfo.InvariantCulture));

        using var response = await client.SendAsync(request, AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return response.Headers.Location!.ToString();
    }

    private static async Task<HttpResponseMessage> _PatchAsync(HttpClient client, string location, byte[] body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", "0");
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");

        return await client.SendAsync(request, AbortToken);
    }

    /// <summary>A file lock that always grants: two requests for one upload proceed together.</summary>
    private sealed class AlwaysGrantedFileLockProvider : ITusFileLockProvider, ITusFileLock
    {
        public Task<ITusFileLock> AquireLock(string fileId)
        {
            return Task.FromResult<ITusFileLock>(this);
        }

        public Task<bool> Lock()
        {
            return Task.FromResult(true);
        }

        public Task ReleaseIfHeld()
        {
            return Task.CompletedTask;
        }
    }
}
