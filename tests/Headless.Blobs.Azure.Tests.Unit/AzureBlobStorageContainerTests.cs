// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Blobs.Azure;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests;

public sealed class AzureBlobContainerManagerTests : TestBase
{
    [Fact]
    public async Task ensure_container_creates_at_most_once()
    {
        var containerClient = Substitute.For<BlobContainerClient>();
        var serviceClient = Substitute.For<BlobServiceClient>();
        serviceClient.GetBlobContainerClient(Arg.Any<string>()).Returns(containerClient);

        // Container lifecycle now lives on the separately-resolved manager, not on AzureBlobStorage.
        using var sut = new AzureBlobContainerManager(
            serviceClient,
            new AzureBlobNamingNormalizer(),
            PublicAccessType.None
        );

        await sut.EnsureContainerAsync("mycontainer", AbortToken);
        await sut.EnsureContainerAsync("mycontainer", AbortToken);

        // The second call is served from the per-instance cache; CreateIfNotExists runs once.
        await containerClient
            .Received(1)
            .CreateIfNotExistsAsync(
                Arg.Any<PublicAccessType>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<BlobContainerEncryptionScopeOptions>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task delete_container_blocks_concurrent_ensure_until_delete_completes()
    {
        var containerClient = Substitute.For<BlobContainerClient>();
        var serviceClient = Substitute.For<BlobServiceClient>();
        serviceClient.GetBlobContainerClient(Arg.Any<string>()).Returns(containerClient);

        var deleteCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelete = new TaskCompletionSource<Response<bool>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        containerClient
            .DeleteIfExistsAsync(Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleteCalled.TrySetResult();

                return releaseDelete.Task;
            });

        using var sut = new AzureBlobContainerManager(
            serviceClient,
            new AzureBlobNamingNormalizer(),
            PublicAccessType.None
        );
        await sut.EnsureContainerAsync("mycontainer", AbortToken);

        // Start the delete and park it inside the backend call; the ensured-cache entry was already evicted under
        // the per-container lock before the backend delete began.
        var deleteTask = sut.DeleteContainerAsync("mycontainer", AbortToken).AsTask();
        await deleteCalled.Task;

        // A concurrent ensure must not be served from the stale cache entry: it misses and waits on the
        // per-container lock held by the in-flight delete instead of reporting success for a container mid-deletion.
        var ensureTask = sut.EnsureContainerAsync("mycontainer", AbortToken).AsTask();
        ensureTask.IsCompleted.Should().BeFalse();

        releaseDelete.SetResult(Response.FromValue(true, Substitute.For<Response>()));
        (await deleteTask).Should().BeTrue();
        await ensureTask;

        // The post-delete ensure re-created the container rather than trusting the pre-delete cache entry.
        await containerClient
            .Received(2)
            .CreateIfNotExistsAsync(
                Arg.Any<PublicAccessType>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<BlobContainerEncryptionScopeOptions>(),
                Arg.Any<CancellationToken>()
            );
    }
}
