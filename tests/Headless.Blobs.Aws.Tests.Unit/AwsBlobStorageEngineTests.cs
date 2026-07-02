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
    public async Task upload_does_not_create_a_missing_bucket()
    {
        // The data plane never auto-creates a missing top-level container; it only writes the object.
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        var sut = _CreateSut();

        using var stream = new MemoryStream("hello"u8.ToArray());
        await sut.UploadAsync(new BlobLocation("bucket", "file.txt"), stream);

        await _s3.DidNotReceiveWithAnyArgs().PutBucketAsync(default(PutBucketRequest)!, default);
        await _s3.ReceivedWithAnyArgs(1).PutObjectAsync(default!, default);
    }

    [Fact]
    public async Task upload_normalizes_bucket_and_preserves_object_key_path()
    {
        PutObjectRequest? captured = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        var sut = _CreateSut();

        using var stream = new MemoryStream("hi"u8.ToArray());
        await sut.UploadAsync(new BlobLocation("My-Bucket", "Reports", "Q1.pdf"), stream);

        // Two-tier naming: the container is lowercased by NormalizeContainerName; the sub-path and blob name are
        // preserved because AwsBlobNamingNormalizer.NormalizeBlobName is validate-only.
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

        var sut = _CreateSut();

        var blobs = Enumerable
            .Range(0, 50)
            .Select(i => new BlobUploadRequest(
                $"{(i % 2 == 0 ? "fail" : "ok")}-{i:000}.txt",
                new MemoryStream("x"u8.ToArray())
            ))
            .ToList();

        var results = await sut.BulkUploadAsync("bucket", blobs);

        results.Should().HaveCount(blobs.Count);

        for (var i = 0; i < blobs.Count; i++)
        {
            var expectedFailure = i % 2 == 0;

            results[i]
                .Result.IsFailure.Should()
                .Be(expectedFailure, "result at index {0} must describe blobs[{0}] ({1})", i, blobs[i].Path);

            // Every result correlates to its own blob by identity (the new contract), not merely by position.
            results[i].Path.Should().Be(blobs[i].Path);

            if (expectedFailure)
            {
                // The carried error must be the one raised for *this* blob, proving slot alignment.
                results[i].Result.Error.Message.Should().Be(blobs[i].Path);
            }
        }
    }

    [Fact]
    public async Task bulk_upload_propagates_cancellation()
    {
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var sut = _CreateSut();
        IReadOnlyCollection<BlobUploadRequest> blobs = [new("cancel.txt", new MemoryStream("x"u8.ToArray()))];

        var act = async () => await sut.BulkUploadAsync("bucket", blobs, AbortToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task bulk_delete_uses_s3_batch_delete_and_maps_delete_errors()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var key = ci.ArgAt<string>(1);

                return key == "absent.txt"
                    ? Task.FromException<GetObjectMetadataResponse>(
                        new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound }
                    )
                    : Task.FromResult(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK });
            });

        _s3.DeleteObjectsAsync(Arg.Any<DeleteObjectsRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var keys = ci.Arg<DeleteObjectsRequest>().Objects.Select(static item => item.Key).ToList();

                return new DeleteObjectsResponse
                {
                    HttpStatusCode = HttpStatusCode.OK,
                    DeletedObjects = keys.Where(static key => key == "ok.txt")
                        .Select(static key => new DeletedObject { Key = key })
                        .ToList(),
                    DeleteErrors = keys.Where(static key => key == "fail.txt")
                        .Select(static key => new DeleteError
                        {
                            Key = key,
                            Code = "InternalError",
                            Message = "transient",
                        })
                        .ToList(),
                };
            });

        var sut = _CreateSut();

        var results = await sut.BulkDeleteAsync(
            "bucket",
            ["ok.txt", "fail.txt", "absent.txt", "../escape.txt"],
            AbortToken
        );

        results.Should().HaveCount(4);
        results[0].Path.Should().Be("ok.txt");
        results[0].Result.Value.Should().BeTrue();
        results[1].Path.Should().Be("fail.txt");
        results[1].Result.IsFailure.Should().BeTrue();
        results[2].Path.Should().Be("absent.txt");
        results[2].Result.Value.Should().BeFalse();
        results[3].Path.Should().Be("../escape.txt");
        results[3].Location.Should().BeNull();
        results[3].Result.IsFailure.Should().BeTrue();

        await _s3.Received(1)
            .DeleteObjectsAsync(
                Arg.Is<DeleteObjectsRequest>(request =>
                    request.BucketName == "bucket"
                    && request.Objects.Count == 2
                    && request.Objects.Any(static item => item.Key == "ok.txt")
                    && request.Objects.Any(static item => item.Key == "fail.txt")
                ),
                Arg.Any<CancellationToken>()
            );
        await _s3.ReceivedWithAnyArgs(3).GetObjectMetadataAsync(default!, default!, default);
        await _s3.DidNotReceiveWithAnyArgs().DeleteObjectAsync(default!, default);
    }

    [Fact]
    public async Task bulk_delete_propagates_cancellation()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3.DeleteObjectsAsync(Arg.Any<DeleteObjectsRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var sut = _CreateSut();

        var act = async () => await sut.BulkDeleteAsync("bucket", ["cancel.txt"], AbortToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
                        DeletedObjects = requestedKeys
                            .Where(k => k.StartsWith("ok-", StringComparison.Ordinal))
                            .Select(k => new DeletedObject { Key = k })
                            .ToList(),
                        DeleteErrors = requestedKeys
                            .Where(k => k.StartsWith("fail-", StringComparison.Ordinal))
                            .Select(k => new DeleteError
                            {
                                Key = k,
                                Code = "InternalError",
                                Message = "transient",
                            })
                            .ToList(),
                    }
                );
            });

        var sut = _CreateSut();

        var deleted = await sut.DeleteAllAsync(new BlobQuery("bucket"));

        // 3 deleted on the first pass + 2 deleted on the retry. The retry deletions must be included in the
        // returned total — the contract returns the number actually deleted, not just the first-pass successes.
        deleted.Should().Be(5);

        // Exactly the initial bulk delete plus a single retry for the failed keys.
        await _s3.ReceivedWithAnyArgs(2).DeleteObjectsAsync(default!, default);
    }

    [Fact]
    public async Task move_rejects_occupied_destination_without_copying_or_deleting()
    {
        // Reject-occupied: when the destination already exists, Move returns false and touches nothing — no copy,
        // no source delete, so a pre-existing destination can never be lost by a move.
        _s3.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK });

        var sut = _CreateSut();

        var moved = await sut.MoveAsync(
            new BlobLocation("bucket", "source.txt"),
            new BlobLocation("bucket", "target.txt")
        );

        moved.Should().BeFalse();
        await _s3.DidNotReceiveWithAnyArgs().CopyObjectAsync(default!, default);
        await _s3.DidNotReceiveWithAnyArgs().DeleteObjectAsync(default!, default);
    }

    [Fact]
    public async Task move_rolls_back_destination_copy_when_source_delete_throws()
    {
        // Destination reads as absent so the move passes the reject-occupied pre-check and proceeds to copy+delete;
        // the source reads as intact so the post-fault re-check confirms rolling back cannot lose data.
        _s3.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                ci.ArgAt<string>(1) == "target.txt"
                    ? Task.FromException<GetObjectMetadataResponse>(
                        new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound }
                    )
                    : Task.FromResult(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK })
            );

        _s3.CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CopyObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        var deleteError = new AmazonS3Exception("delete failed");

        _s3.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var request = ci.Arg<DeleteObjectRequest>();

                return request.Key == "source.txt"
                    ? Task.FromException<DeleteObjectResponse>(deleteError)
                    : Task.FromResult(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.OK });
            });

        var sut = _CreateSut();

        var act = async () =>
            await sut.MoveAsync(new BlobLocation("bucket", "source.txt"), new BlobLocation("bucket", "target.txt"));

        await act.Should().ThrowAsync<AmazonS3Exception>().WithMessage("delete failed");
        await _s3.Received(1)
            .DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.BucketName == "bucket" && request.Key == "target.txt"),
                Arg.Is<CancellationToken>(token => token == CancellationToken.None)
            );
    }

    [Fact]
    public async Task move_keeps_destination_and_returns_true_when_source_delete_reports_non_success()
    {
        // Destination reads as absent so the move passes the reject-occupied pre-check and proceeds to copy+delete.
        // The source delete completes without throwing but reports a non-2xx status — AwsBlobStorage maps that to
        // the helper's delete-false branch (source already absent / concurrent-delete race): the destination
        // already holds the data, so the move is complete. No rollback, no source re-check.
        _s3.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                ci.ArgAt<string>(1) == "target.txt"
                    ? Task.FromException<GetObjectMetadataResponse>(
                        new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound }
                    )
                    : Task.FromResult(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK })
            );

        _s3.CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CopyObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        _s3.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.InternalServerError });

        var sut = _CreateSut();

        var moved = await sut.MoveAsync(
            new BlobLocation("bucket", "source.txt"),
            new BlobLocation("bucket", "target.txt")
        );

        moved.Should().BeTrue();

        // Exactly one delete — the source. The destination copy is kept: no rollback delete of target.txt.
        await _s3.Received(1).DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>());
        await _s3.Received(1)
            .DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.BucketName == "bucket" && request.Key == "source.txt"),
                Arg.Any<CancellationToken>()
            );

        // The delete-false branch never re-checks the source; only the destination pre-check hits metadata.
        await _s3.Received(1).GetObjectMetadataAsync("bucket", "target.txt", Arg.Any<CancellationToken>());
        await _s3.DidNotReceive().GetObjectMetadataAsync("bucket", "source.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task exists_returns_false_when_bucket_missing()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        var sut = _CreateSut();

        (await sut.ExistsAsync(new BlobLocation("missing", "file.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task get_blob_info_returns_null_when_bucket_missing()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        var sut = _CreateSut();

        (await sut.GetBlobInfoAsync(new BlobLocation("missing", "file.txt"))).Should().BeNull();
    }

    [Fact]
    public async Task open_read_stream_returns_null_when_bucket_missing()
    {
        _s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("no such bucket") { StatusCode = HttpStatusCode.NotFound });

        var sut = _CreateSut();

        (await sut.OpenReadStreamAsync(new BlobLocation("missing", "file.txt"))).Should().BeNull();
    }

    [Fact]
    public async Task presigned_download_throws_on_non_positive_expiry()
    {
        var sut = _CreateSut();

        var act = async () =>
            await sut.GetPresignedDownloadUrlAsync(new BlobLocation("bucket", "file.txt"), TimeSpan.Zero);

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

        var url = await sut.GetPresignedDownloadUrlAsync(
            new BlobLocation("bucket", "file.txt"),
            TimeSpan.FromMinutes(15)
        );

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

        var url = await sut.GetPresignedUploadUrlAsync(
            new BlobLocation("bucket", "file.txt"),
            TimeSpan.FromMinutes(15)
        );

        url.Should().Be(new Uri("https://example.com/signed-put"));
        await _s3.Received(1)
            .GetPreSignedURLAsync(Arg.Is<GetPreSignedUrlRequest>(r => r.Verb == HttpVerb.PUT && r.Key == "file.txt"));
    }
}
