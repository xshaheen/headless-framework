// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.SshNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed partial class SshBlobStorageTests
{
    [Fact]
    public async Task upload_beyond_the_upload_budget_waits_for_an_upload_connection()
    {
        // given: one upload-stream connection, held by a stalled upload, and a free operation pool
        var options = new SshBlobStorageOptions
        {
            ConnectionString = fixture.GetConnectionString(),
            MaxPoolSize = 2,
            MaxConcurrentOperations = 2,
            MaxUploadStreamConnections = 1,
        };
        using var pool = new SftpClientPool(
            new OptionsWrapper<SshBlobStorageOptions>(options),
            NullLogger<SftpClientPool>.Instance
        );
        await using var storage = _CreateStorage(pool, options);
        await ResetAsync(storage);

        await using var stalledBody = new StalledStream(seekable: false);
        var stalledUpload = storage
            .UploadAsync(new BlobLocation(ContainerName, "slow.bin"), stalledBody, cancellationToken: AbortToken)
            .AsTask();

        try
        {
            await stalledBody.Started.WaitAsync(AbortToken);

            // when: a second network-backed upload starts while the only upload connection is held
            using var shortWait = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
            shortWait.CancelAfter(TimeSpan.FromMilliseconds(500));
            await using var waitingBody = new StalledStream(seekable: true);
            var blocked = async () =>
                await storage.UploadAsync(
                    new BlobLocation(ContainerName, "waiting.bin"),
                    waitingBody,
                    cancellationToken: shortWait.Token
                );

            // then: it waits for an upload connection instead of taking one of the free operation connections
            await blocked.Should().ThrowAsync<OperationCanceledException>();
            waitingBody.Started.IsCompleted.Should().BeFalse();

            // and an in-memory upload still takes the operation pool
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
            bounded.CancelAfter(TimeSpan.FromSeconds(30));
            await storage.UploadContentAsync(new BlobLocation(ContainerName, "memory.txt"), "memory", bounded.Token);
        }
        finally
        {
            stalledBody.Finish();
            await stalledUpload;
        }

        (await storage.ExistsAsync(new BlobLocation(ContainerName, "memory.txt"), AbortToken)).Should().BeTrue();
    }
}
