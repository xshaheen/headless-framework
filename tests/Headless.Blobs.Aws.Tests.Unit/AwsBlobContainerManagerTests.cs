// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.Testing.Tests;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// Unit coverage for the AWS bucket-lifecycle capability. Container management was split off <see cref="IBlobStorage"/>
/// onto the separately-registered <see cref="IBlobContainerManager"/>; the ensure-once caching, idempotency, and
/// retry-on-failure logic now live on <see cref="AwsBlobContainerManager"/> and are exercised here against a mocked
/// <see cref="IAmazonS3"/>.
/// </summary>
public sealed class AwsBlobContainerManagerTests : TestBase
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private AwsBlobContainerManager _CreateSut() => new(_s3, new AwsBlobNamingNormalizer());

    [Fact]
    public async Task ensure_container_ensures_bucket_at_most_once()
    {
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>()).Returns(new PutBucketResponse());

        using var sut = _CreateSut();

        await sut.EnsureContainerAsync("bucket", AbortToken);
        var callsAfterFirst = _s3.ReceivedCalls().Count();

        await sut.EnsureContainerAsync("bucket", AbortToken);

        // The second call is served from the per-instance cache and issues no further S3 calls.
        _s3.ReceivedCalls().Should().HaveCount(callsAfterFirst);
    }

    [Fact]
    public async Task concurrent_ensure_container_ensures_bucket_at_most_once()
    {
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>()).Returns(new PutBucketResponse());
        using var sut = _CreateSut();

        // 20 concurrent first-time ensures of the same bucket.
        await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => sut.EnsureContainerAsync("bucket", AbortToken).AsTask())
        );
        var concurrentCalls = _s3.ReceivedCalls().Count();

        // A single ensure on a fresh instance issues the same S3 calls; concurrency must not multiply them.
        var fresh = Substitute.For<IAmazonS3>();
        fresh
            .PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutBucketResponse());
        using var freshSut = new AwsBlobContainerManager(fresh, new AwsBlobNamingNormalizer());
        await freshSut.EnsureContainerAsync("bucket", AbortToken);

        concurrentCalls.Should().Be(fresh.ReceivedCalls().Count());
    }

    [Fact]
    public async Task ensure_container_is_idempotent_when_bucket_already_owned()
    {
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("already owned") { ErrorCode = "BucketAlreadyOwnedByYou" });

        using var sut = _CreateSut();

        var act = async () => await sut.EnsureContainerAsync("bucket", AbortToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task bucket_create_failure_is_not_cached()
    {
        var calls = 0;
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;

                return calls == 1
                    ? Task.FromException<PutBucketResponse>(
                        new AmazonS3Exception("transient") { StatusCode = HttpStatusCode.ServiceUnavailable }
                    )
                    : Task.FromResult(new PutBucketResponse());
            });

        using var sut = _CreateSut();

        // First ensure fails; the failure must not be cached.
        var firstAttempt = async () => await sut.EnsureContainerAsync("bucket", AbortToken);
        await firstAttempt.Should().ThrowAsync<AmazonS3Exception>();

        // Retry re-attempts the create rather than serving a poisoned cache entry.
        await sut.EnsureContainerAsync("bucket", AbortToken);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task container_exists_returns_false_when_bucket_missing()
    {
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        using var sut = _CreateSut();

        (await sut.ContainerExistsAsync("missing", AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task container_exists_returns_true_when_bucket_reachable()
    {
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { HttpStatusCode = HttpStatusCode.OK });

        using var sut = _CreateSut();

        (await sut.ContainerExistsAsync("present", AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task delete_container_returns_false_when_bucket_missing()
    {
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        using var sut = _CreateSut();

        (await sut.DeleteContainerAsync("missing", AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task delete_container_blocks_concurrent_ensure_until_delete_completes()
    {
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>()).Returns(new PutBucketResponse());
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response());

        var deleteBucketCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelete = new TaskCompletionSource<DeleteBucketResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _s3.DeleteBucketAsync(Arg.Any<DeleteBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleteBucketCalled.TrySetResult();

                return releaseDelete.Task;
            });

        using var sut = _CreateSut();
        await sut.EnsureContainerAsync("bucket", AbortToken);

        // Start the delete and park it inside the backend call; the ensured-cache entry was already evicted under
        // the per-bucket lock before the backend delete began.
        var deleteTask = sut.DeleteContainerAsync("bucket", AbortToken).AsTask();
        await deleteBucketCalled.Task;

        // A concurrent ensure must not be served from the stale cache entry: it misses and waits on the per-bucket
        // lock held by the in-flight delete instead of reporting success for a bucket mid-deletion.
        var ensureTask = sut.EnsureContainerAsync("bucket", AbortToken).AsTask();
        ensureTask.IsCompleted.Should().BeFalse();

        releaseDelete.SetResult(new DeleteBucketResponse());
        (await deleteTask).Should().BeTrue();
        await ensureTask;

        // The post-delete ensure re-created the bucket rather than trusting the pre-delete cache entry.
        await _s3.Received(2).PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>());
    }
}
