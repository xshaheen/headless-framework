// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AwsBlobStorageEngineTests : TestBase
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private AwsBlobStorage _CreateSut(AwsBlobStorageOptions? options = null)
    {
        var wrapper = new OptionsWrapper<AwsBlobStorageOptions>(options ?? new AwsBlobStorageOptions());

        return new AwsBlobStorage(
            _s3,
            new MimeTypeProvider(),
            new Clock(TimeProvider.System),
            wrapper,
            new AwsBlobNamingNormalizer()
        );
    }

    [Fact]
    public async Task upload_does_not_create_bucket_when_auto_create_disabled()
    {
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        var sut = _CreateSut(new AwsBlobStorageOptions { AutoCreateContainer = false });

        using var stream = new MemoryStream("hello"u8.ToArray());
        await sut.UploadAsync(["bucket"], "file.txt", stream);

        await _s3.DidNotReceiveWithAnyArgs().PutBucketAsync(default(PutBucketRequest)!, default);
        await _s3.ReceivedWithAnyArgs(1).PutObjectAsync(default!, default);
    }

    [Fact]
    public async Task upload_normalizes_bucket_and_preserves_object_key_path()
    {
        PutObjectRequest? captured = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        var sut = _CreateSut(new AwsBlobStorageOptions { AutoCreateContainer = false });

        using var stream = new MemoryStream("hi"u8.ToArray());
        await sut.UploadAsync(["My-Bucket", "Reports"], "Q1.pdf", stream);

        // Two-tier naming: the first segment (bucket) is lowercased by NormalizeContainerName; the sub-path and
        // blob name are preserved because AwsBlobNamingNormalizer.NormalizeBlobName is validate-only.
        captured.Should().NotBeNull();
        captured!.BucketName.Should().Be("my-bucket");
        captured.Key.Should().Be("Reports/Q1.pdf");
    }

    [Fact]
    public async Task bulk_upload_returns_results_aligned_to_input_blob_order_under_failures()
    {
        // Blobs at even indices are configured to fail, odd indices to succeed. results[i] must describe
        // blobs[i] regardless of the order the parallel upload bodies happen to start under MaxBulkParallelism.
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var key = ci.Arg<PutObjectRequest>().Key;

                return key.StartsWith("fail-", StringComparison.Ordinal)
                    ? Task.FromException<PutObjectResponse>(new AmazonS3Exception(key))
                    : Task.FromResult(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
            });

        var sut = _CreateSut(new AwsBlobStorageOptions { AutoCreateContainer = false });

        var blobs = Enumerable
            .Range(0, 50)
            .Select(i => new BlobUploadRequest(
                new MemoryStream("x"u8.ToArray()),
                $"{(i % 2 == 0 ? "fail" : "ok")}-{i:000}.txt"
            ))
            .ToList();

        var results = await sut.BulkUploadAsync(["bucket"], blobs);

        results.Should().HaveCount(blobs.Count);

        for (var i = 0; i < blobs.Count; i++)
        {
            var expectedFailure = i % 2 == 0;

            results[i]
                .IsFailure.Should()
                .Be(expectedFailure, "result at index {0} must describe blobs[{0}] ({1})", i, blobs[i].FileName);

            if (expectedFailure)
            {
                // The carried error must be the one raised for *this* blob, proving slot alignment.
                results[i].Error.Message.Should().Be(blobs[i].FileName);
            }
        }
    }

    [Fact]
    public async Task delete_all_counts_objects_deleted_on_the_partial_failure_retry()
    {
        // A single listing page of five matching objects: three that delete on the first pass, two that fail
        // and only succeed on the one-time retry.
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListObjectsV2Response
                {
                    HttpStatusCode = HttpStatusCode.OK,
                    IsTruncated = false,
                    S3Objects =
                    [
                        new() { Key = "ok-0" },
                        new() { Key = "ok-1" },
                        new() { Key = "ok-2" },
                        new() { Key = "fail-0" },
                        new() { Key = "fail-1" },
                    ],
                }
            );

        // First DeleteObjects (all five keys) reports a partial failure — three deleted, two errored. The retry,
        // issued only for the two "fail-" keys, then deletes them. Branch on the request content (like
        // bulk_upload_returns_results_aligned_to_input_blob_order_under_failures) instead of relying on call order.
        _s3.DeleteObjectsAsync(Arg.Any<DeleteObjectsRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var requestedKeys = (ci.Arg<DeleteObjectsRequest>().Objects ?? [])
                    .Select(o => o.Key)
                    .Where(k => k is not null)
                    .Select(k => k!)
                    .ToList();

                var isRetry = requestedKeys.TrueForAll(k => k.StartsWith("fail-", StringComparison.Ordinal));

                if (isRetry)
                {
                    // Retry deletes the previously-failed objects successfully, leaving no remaining errors.
                    return Task.FromResult(
                        new DeleteObjectsResponse
                        {
                            HttpStatusCode = HttpStatusCode.OK,
                            DeletedObjects = requestedKeys.ConvertAll(k => new DeletedObject { Key = k }),
                        }
                    );
                }

                return Task.FromResult(
                    new DeleteObjectsResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        DeletedObjects =
                        [
                            .. requestedKeys
                                .Where(k => k.StartsWith("ok-", StringComparison.Ordinal))
                                .Select(k => new DeletedObject { Key = k }),
                        ],
                        DeleteErrors =
                        [
                            .. requestedKeys
                                .Where(k => k.StartsWith("fail-", StringComparison.Ordinal))
                                .Select(k => new DeleteError
                                {
                                    Key = k,
                                    Code = "InternalError",
                                    Message = "transient",
                                }),
                        ],
                    }
                );
            });

        var sut = _CreateSut(new AwsBlobStorageOptions { AutoCreateContainer = false });

        var deleted = await sut.DeleteAllAsync(["bucket"]);

        // 3 deleted on the first pass + 2 deleted on the retry. The retry deletions must be included in the
        // returned total — the contract returns the number actually deleted, not just the first-pass successes.
        deleted.Should().Be(5);

        // Exactly the initial bulk delete plus a single retry for the failed keys.
        await _s3.ReceivedWithAnyArgs(2).DeleteObjectsAsync(default!, default);
    }

    [Fact]
    public async Task exists_returns_false_when_bucket_missing()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        var sut = _CreateSut();

        (await sut.ExistsAsync(["missing"], "file.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task get_blob_info_returns_null_when_bucket_missing()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        var sut = _CreateSut();

        (await sut.GetBlobInfoAsync(["missing"], "file.txt")).Should().BeNull();
    }

    [Fact]
    public async Task open_read_stream_returns_null_when_bucket_missing()
    {
        _s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        var sut = _CreateSut();

        (await sut.OpenReadStreamAsync(["missing"], "file.txt")).Should().BeNull();
    }

    [Fact]
    public async Task create_container_ensures_bucket_at_most_once()
    {
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>()).Returns(new PutBucketResponse());

        var sut = _CreateSut();

        await sut.CreateContainerAsync(["bucket"]);
        var callsAfterFirst = _s3.ReceivedCalls().Count();

        await sut.CreateContainerAsync(["bucket"]);

        // The second call is served from the per-instance cache and issues no further S3 calls.
        _s3.ReceivedCalls().Should().HaveCount(callsAfterFirst);
    }

    [Fact]
    public async Task concurrent_create_container_ensures_bucket_at_most_once()
    {
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>()).Returns(new PutBucketResponse());
        var sut = _CreateSut();

        // 20 concurrent first-time ensures of the same bucket.
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => sut.CreateContainerAsync(["bucket"]).AsTask()));
        var concurrentCalls = _s3.ReceivedCalls().Count();

        // A single ensure on a fresh instance issues the same S3 calls; concurrency must not multiply them.
        var fresh = Substitute.For<IAmazonS3>();
        fresh
            .PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutBucketResponse());
        var freshSut = new AwsBlobStorage(
            fresh,
            new MimeTypeProvider(),
            new Clock(TimeProvider.System),
            new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions()),
            new AwsBlobNamingNormalizer()
        );
        await freshSut.CreateContainerAsync(["bucket"]);

        concurrentCalls.Should().Be(fresh.ReceivedCalls().Count());
    }

    [Fact]
    public async Task create_container_is_idempotent_when_bucket_already_owned()
    {
        _s3.PutBucketAsync(Arg.Any<PutBucketRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("already owned") { ErrorCode = "BucketAlreadyOwnedByYou" });

        var sut = _CreateSut();

        var act = async () => await sut.CreateContainerAsync(["bucket"]);

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

        var sut = _CreateSut();

        // First ensure fails; the failure must not be cached.
        var firstAttempt = async () => await sut.CreateContainerAsync(["bucket"]);
        await firstAttempt.Should().ThrowAsync<AmazonS3Exception>();

        // Retry re-attempts the create rather than serving a poisoned cache entry.
        await sut.CreateContainerAsync(["bucket"]);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task presigned_download_throws_on_non_positive_expiry()
    {
        var sut = _CreateSut();

        var act = async () => await sut.GetPresignedDownloadUrlAsync(["bucket"], "file.txt", TimeSpan.Zero);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void implements_presigned_url_capability()
    {
        _CreateSut().Should().BeAssignableTo<IPresignedUrlBlobStorage>();
    }

    [Fact]
    public async Task presigned_download_url_uses_get_verb_for_the_blob()
    {
        _s3.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://example.com/signed-get");

        var sut = _CreateSut();

        var url = await sut.GetPresignedDownloadUrlAsync(["bucket"], "file.txt", TimeSpan.FromMinutes(15));

        url.Should().Be(new Uri("https://example.com/signed-get"));
        await _s3.Received(1)
            .GetPreSignedURLAsync(
                Arg.Is<GetPreSignedUrlRequest>(r =>
                    r.Verb == HttpVerb.GET
                    && r.BucketName == "bucket"
                    && r.Key == "file.txt"
                    // Expires is the absolute deadline: roughly now + the requested 15-minute window.
                    && r.Expires > DateTime.UtcNow.AddMinutes(14)
                    && r.Expires < DateTime.UtcNow.AddMinutes(16)
                )
            );
    }

    [Fact]
    public async Task presigned_upload_url_uses_put_verb_for_the_blob()
    {
        _s3.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://example.com/signed-put");

        var sut = _CreateSut();

        var url = await sut.GetPresignedUploadUrlAsync(["bucket"], "file.txt", TimeSpan.FromMinutes(15));

        url.Should().Be(new Uri("https://example.com/signed-put"));
        await _s3.Received(1)
            .GetPreSignedURLAsync(Arg.Is<GetPreSignedUrlRequest>(r => r.Verb == HttpVerb.PUT && r.Key == "file.txt"));
    }
}
