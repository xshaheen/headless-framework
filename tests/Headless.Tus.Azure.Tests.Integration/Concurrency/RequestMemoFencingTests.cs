// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;
using Tests.TestSetup;

namespace Tests.Concurrency;

/// <summary>
/// The store calls of one tus request share what the first call read. These tests run a request's calls under one
/// <see cref="HttpContext" /> while a second store, outside any request, writes the same blob in between, and pin that
/// the shared read never hides that write from a fence, and that a rejected write makes the request read again.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class RequestMemoFencingTests(TusAzureFixture fixture) : TestBase
{
    private readonly HttpContextAccessor _accessor = new();
    private TusAzureStore _requestStore = null!;
    private TusAzureStore _otherWriter = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var client = fixture.CreateBlobServiceClient();
        var options = new TusAzureStoreOptions
        {
            ContainerName = "tusmemofencing",
            BlobPrefix = $"{Guid.NewGuid():N}/",
        };
        await new TusAzureContainerProvisioner(client, options).ProvisionAsync(AbortToken);

        _requestStore = new TusAzureStore(
            client,
            options,
            loggerFactory: LoggerFactory,
            httpContextAccessor: _accessor
        );
        _otherWriter = new TusAzureStore(client, options, loggerFactory: LoggerFactory);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _accessor.HttpContext = null;

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_reject_a_write_built_on_the_request_read_and_read_again_after()
    {
        // given - a deferred-length upload the request has already read
        var fileId = await _otherWriter.CreateFileAsync(-1, metadata: null, AbortToken);
        _accessor.HttpContext = new DefaultHttpContext();
        (await _requestStore.GetUploadLengthAsync(fileId, AbortToken)).Should().BeNull();

        // when - another writer declares the length, then the request writes from its earlier read
        await _otherWriter.SetUploadLengthAsync(fileId, 500, AbortToken);
        var act = () => _requestStore.SetUploadLengthAsync(fileId, 700, AbortToken);

        // then - the stale write is rejected, and the request's next call reads the blob again
        (await act.Should().ThrowExactlyAsync<RequestFailedException>())
            .Which.Status.Should()
            .Be(412);
        (await _requestStore.GetUploadLengthAsync(fileId, AbortToken)).Should().Be(500);
    }

    [Fact]
    public async Task should_reject_an_append_built_on_the_request_read()
    {
        // given - the request read the upload before another writer appended
        var fileId = await _otherWriter.CreateFileAsync(1_000, metadata: null, AbortToken);
        _accessor.HttpContext = new DefaultHttpContext();
        (await _requestStore.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);

        await using (var other = new MemoryStream(Faker.Random.Bytes(300)))
        {
            await _otherWriter.AppendDataAsync(fileId, other, AbortToken);
        }

        // when - the request appends from its earlier read
        var act = async () =>
        {
            await using var body = new MemoryStream(Faker.Random.Bytes(200));
            await _requestStore.AppendDataAsync(fileId, body, AbortToken);
        };

        // then - the commit fails instead of replacing the other writer's block list, which stays committed
        (await act.Should().ThrowExactlyAsync<RequestFailedException>())
            .Which.Status.Should()
            .Be(412);
        (await _requestStore.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(300);
        _accessor.HttpContext = null;
        (await _otherWriter.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(300);
    }

    [Fact]
    public async Task should_retry_the_expiration_on_the_blob_another_writer_changed()
    {
        // given - the request read the upload before another writer declared its length
        var fileId = await _otherWriter.CreateFileAsync(-1, metadata: null, AbortToken);
        _accessor.HttpContext = new DefaultHttpContext();
        (await _requestStore.GetUploadLengthAsync(fileId, AbortToken)).Should().BeNull();
        await _otherWriter.SetUploadLengthAsync(fileId, 500, AbortToken);

        // when - the request refreshes the expiration from its earlier read
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        await _requestStore.SetExpirationAsync(fileId, expires, AbortToken);

        // then - the write retried against the current metadata, so both writes hold
        _accessor.HttpContext = null;
        (await _otherWriter.GetUploadLengthAsync(fileId, AbortToken)).Should().Be(500);
        (await _otherWriter.GetExpirationAsync(fileId, AbortToken))
            .Should()
            .BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_answer_offset_and_expiration_from_the_request_commit()
    {
        // given
        var fileId = await _otherWriter.CreateFileAsync(500, metadata: null, AbortToken);
        _accessor.HttpContext = new DefaultHttpContext();
        await _requestStore.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);

        // when - the request appends the whole upload
        await using (var body = new MemoryStream(Faker.Random.Bytes(500)))
        {
            await _requestStore.AppendDataAsync(fileId, body, AbortToken);
        }

        // then - its later calls see the length its own commit made: complete, so no expiration
        (await _requestStore.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(500);
        (await _requestStore.GetExpirationAsync(fileId, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_forget_a_deleted_upload()
    {
        // given
        var fileId = await _otherWriter.CreateFileAsync(500, metadata: null, AbortToken);
        _accessor.HttpContext = new DefaultHttpContext();
        (await _requestStore.FileExistAsync(fileId, AbortToken)).Should().BeTrue();

        // when
        await _requestStore.DeleteFileAsync(fileId, AbortToken);

        // then
        (await _requestStore.FileExistAsync(fileId, AbortToken))
            .Should()
            .BeFalse();
    }
}
