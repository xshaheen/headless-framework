// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// Copy and move of a source above the single-copy limit, which S3 accepts only as a multipart copy: the destination
/// keeps the source's metadata, every part is pinned to the version the metadata read saw, any failure aborts the
/// upload, and a move deletes only the version it copied.
/// </summary>
public sealed class AwsBlobStorageMultipartCopyTests : TestBase
{
    private const long _MiB = 1024 * 1024;
    private const long _GiB = 1024 * _MiB;
    private const string _SourceETag = "\"source-etag\"";

    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private static readonly BlobLocation _Source = new("bucket", "big/source.bin");
    private static readonly BlobLocation _Destination = new("bucket", "big/target.bin");

    private AwsBlobStorage _CreateSut(long threshold = 1 * _MiB, bool supportsSourcePreconditions = true)
    {
        return new AwsBlobStorage(
            _s3,
            new MimeTypeProvider(),
            TimeProvider.System,
            new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions()),
            new AwsBlobNamingNormalizer()
        )
        {
            MultipartCopyThreshold = threshold,
            SupportsSourcePreconditions = supportsSourcePreconditions,
        };
    }

    private void _ArrangeSource(long size, string? versionId = null)
    {
        var head = new GetObjectMetadataResponse
        {
            HttpStatusCode = HttpStatusCode.OK,
            ContentLength = size,
            ETag = _SourceETag,
            VersionId = versionId,
        };
        head.Headers.ContentType = "application/x-custom";
        head.Headers.CacheControl = "no-cache";
        head.Headers.ContentDisposition = "attachment; filename=\"source.bin\"";
        head.Metadata["x-amz-meta-uploaddate"] = "2026-01-02T03:04:05.0000000+00:00";
        head.Metadata["x-amz-meta-owner"] = "alice";

        _s3.GetObjectMetadataAsync(
                Arg.Is<GetObjectMetadataRequest>(request => request.Key == "big/source.bin"),
                Arg.Any<CancellationToken>()
            )
            .Returns(head);
    }

    private void _ArrangeMultipart()
    {
        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { HttpStatusCode = HttpStatusCode.OK, UploadId = "upload-1" });
        _s3.CopyPartAsync(Arg.Any<CopyPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                Task.FromResult(
                    new CopyPartResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        ETag = $"etag-{ci.Arg<CopyPartRequest>().PartNumber}",
                    }
                )
            );
        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteMultipartUploadResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3.AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AbortMultipartUploadResponse { HttpStatusCode = HttpStatusCode.OK });
    }

    private List<CopyPartRequest> _CopiedParts()
    {
        return
        [
            .. _s3.ReceivedCalls()
                .Where(call =>
                    string.Equals(call.GetMethodInfo().Name, nameof(IAmazonS3.CopyPartAsync), StringComparison.Ordinal)
                )
                .Select(call => (CopyPartRequest)call.GetArguments()[0]!)
                .OrderBy(request => request.PartNumber),
        ];
    }

    [Fact]
    public async Task copy_above_the_threshold_keeps_metadata_content_type_and_system_headers()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        var sut = _CreateSut();

        var copied = await sut.CopyAsync(_Source, _Destination, AbortToken);

        copied.Should().BeTrue();
        await _s3.Received(1)
            .InitiateMultipartUploadAsync(
                Arg.Is<InitiateMultipartUploadRequest>(request =>
                    request.BucketName == "bucket"
                    && request.Key == "big/target.bin"
                    && request.ContentType == "application/x-custom"
                    && request.Headers.CacheControl == "no-cache"
                    && request.Headers.ContentDisposition == "attachment; filename=\"source.bin\""
                    && request.Metadata["owner"] == "alice"
                    && request.Metadata["uploaddate"] == "2026-01-02T03:04:05.0000000+00:00"
                    && request.Metadata.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );

        // 12 MiB splits into the 5 MiB minimum part size: 5 + 5 + 2 MiB.
        _CopiedParts()
            .Select(part => (part.FirstByte, part.LastByte))
            .Should()
            .Equal((0L, (5 * _MiB) - 1), (5 * _MiB, (10 * _MiB) - 1), (10 * _MiB, (12 * _MiB) - 1));
    }

    [Fact]
    public async Task copy_pins_every_part_to_the_source_etag_when_the_source_is_unversioned()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        var sut = _CreateSut();

        await sut.CopyAsync(_Source, _Destination, AbortToken);

        _CopiedParts()
            .Should()
            .AllSatisfy(part =>
            {
                part.ETagToMatch.Should().Equal(_SourceETag);
                part.SourceVersionId.Should().BeNull();
            });
    }

    [Fact]
    public async Task copy_pins_every_part_to_the_source_version_when_the_source_is_versioned()
    {
        _ArrangeSource(12 * _MiB, versionId: "v-1");
        _ArrangeMultipart();
        var sut = _CreateSut();

        await sut.CopyAsync(_Source, _Destination, AbortToken);

        _CopiedParts()
            .Should()
            .AllSatisfy(part =>
            {
                part.SourceVersionId.Should().Be("v-1");
                part.ETagToMatch.Should().BeNullOrEmpty();
            });
    }

    [Fact]
    public async Task copy_sends_no_part_precondition_when_the_endpoint_does_not_support_one()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        var sut = _CreateSut(supportsSourcePreconditions: false);

        var copied = await sut.CopyAsync(_Source, _Destination, AbortToken);

        copied.Should().BeTrue();
        _CopiedParts().Should().AllSatisfy(part => part.ETagToMatch.Should().BeNullOrEmpty());
    }

    [Fact]
    public async Task copy_aborts_the_upload_and_throws_when_the_source_changes_mid_copy()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.CopyPartAsync(Arg.Is<CopyPartRequest>(request => request.PartNumber == 2), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new AmazonS3Exception("At least one of the pre-conditions you specified did not hold")
                {
                    StatusCode = HttpStatusCode.PreconditionFailed,
                }
            );
        var sut = _CreateSut();

        var act = async () => await sut.CopyAsync(_Source, _Destination, AbortToken);

        (await act.Should().ThrowAsync<AmazonS3Exception>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.PreconditionFailed);
        await _s3.Received(1)
            .AbortMultipartUploadAsync(
                Arg.Is<AbortMultipartUploadRequest>(request =>
                    request.UploadId == "upload-1" && request.Key == "big/target.bin"
                ),
                CancellationToken.None
            );
        await _s3.DidNotReceiveWithAnyArgs().CompleteMultipartUploadAsync(default!, AbortToken);
    }

    [Fact]
    public async Task copy_aborts_the_upload_and_returns_false_when_the_source_is_deleted_mid_copy()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.CopyPartAsync(Arg.Any<CopyPartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("NoSuchKey") { StatusCode = HttpStatusCode.NotFound });
        var sut = _CreateSut();

        var copied = await sut.CopyAsync(_Source, _Destination, AbortToken);

        copied.Should().BeFalse();
        await _s3.Received(1).AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), CancellationToken.None);
        await _s3.DidNotReceiveWithAnyArgs().CompleteMultipartUploadAsync(default!, AbortToken);
    }

    [Fact]
    public async Task copy_returns_false_when_the_destination_bucket_is_missing()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("NoSuchBucket") { StatusCode = HttpStatusCode.NotFound });
        var sut = _CreateSut();

        var copied = await sut.CopyAsync(_Source, _Destination, AbortToken);

        copied.Should().BeFalse();
        await _s3.DidNotReceiveWithAnyArgs().CopyPartAsync(default!, AbortToken);
        await _s3.DidNotReceiveWithAnyArgs().AbortMultipartUploadAsync(default!, AbortToken);
    }

    [Fact]
    public async Task move_keeps_the_destination_when_the_conditional_source_delete_finds_the_source_gone()
    {
        // given: the source was deleted after the copy, so the If-Match delete reports 404.
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.GetObjectMetadataAsync("bucket", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });
        _s3.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("NoSuchKey") { StatusCode = HttpStatusCode.NotFound });
        var sut = _CreateSut();

        // when
        var moved = await sut.MoveAsync(_Source, _Destination, AbortToken);

        // then: the copy is the only surviving version, so it is kept.
        moved.Should().BeTrue();
        await _s3.DidNotReceive()
            .DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.Key == "big/target.bin"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task copy_aborts_the_upload_when_completing_it_fails()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("complete failed") { StatusCode = HttpStatusCode.InternalServerError });
        var sut = _CreateSut();

        var act = async () => await sut.CopyAsync(_Source, _Destination, AbortToken);

        await act.Should().ThrowAsync<AmazonS3Exception>().WithMessage("complete failed");
        await _s3.Received(1).AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), CancellationToken.None);
    }

    [Fact]
    public async Task copy_at_or_below_the_threshold_sends_one_copy_object()
    {
        _ArrangeSource(1 * _MiB);
        _s3.CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CopyObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        var sut = _CreateSut();

        var copied = await sut.CopyAsync(_Source, _Destination, AbortToken);

        copied.Should().BeTrue();
        await _s3.Received(1)
            .CopyObjectAsync(
                Arg.Is<CopyObjectRequest>(request =>
                    request.SourceKey == "big/source.bin"
                    && request.DestinationKey == "big/target.bin"
                    && request.MetadataDirective == S3MetadataDirective.COPY
                ),
                Arg.Any<CancellationToken>()
            );
        await _s3.DidNotReceiveWithAnyArgs().InitiateMultipartUploadAsync(default!, AbortToken);
    }

    [Fact]
    public async Task copy_returns_false_without_copying_when_the_source_is_missing()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });
        var sut = _CreateSut();

        var copied = await sut.CopyAsync(_Source, _Destination, AbortToken);

        copied.Should().BeFalse();
        await _s3.DidNotReceiveWithAnyArgs().CopyObjectAsync(default!, AbortToken);
        await _s3.DidNotReceiveWithAnyArgs().InitiateMultipartUploadAsync(default!, AbortToken);
    }

    [Fact]
    public async Task move_above_the_threshold_copies_then_deletes_only_the_copied_source_version()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.GetObjectMetadataAsync("bucket", "big/target.bin", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });
        _s3.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });
        var sut = _CreateSut();

        var moved = await sut.MoveAsync(_Source, _Destination, AbortToken);

        moved.Should().BeTrue();
        await _s3.Received(1)
            .CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>());
        await _s3.Received(1)
            .DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request =>
                    request.Key == "big/source.bin" && request.IfMatch == _SourceETag
                ),
                Arg.Any<CancellationToken>()
            );
        await _s3.DidNotReceive()
            .DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.Key == "big/target.bin"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task move_keeps_an_overwritten_source_and_rolls_the_copy_back()
    {
        // given: the source changed after the copy read it, so the conditional delete fails with 412.
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.GetObjectMetadataAsync("bucket", "big/target.bin", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });
        _s3.GetObjectMetadataAsync("bucket", "big/source.bin", Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3.DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.Key == "big/source.bin"),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new AmazonS3Exception("precondition failed") { StatusCode = HttpStatusCode.PreconditionFailed }
            );
        _s3.DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.Key == "big/target.bin"),
                Arg.Any<CancellationToken>()
            )
            .Returns(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });
        var sut = _CreateSut();

        // when
        var act = async () => await sut.MoveAsync(_Source, _Destination, AbortToken);

        // then: the newer source stays, and the copy of the older version is removed.
        (await act.Should().ThrowAsync<AmazonS3Exception>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.PreconditionFailed);
        await _s3.Received(1)
            .DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.Key == "big/target.bin"),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task move_deletes_the_source_unconditionally_when_the_endpoint_does_not_support_preconditions()
    {
        _ArrangeSource(12 * _MiB);
        _ArrangeMultipart();
        _s3.GetObjectMetadataAsync("bucket", "big/target.bin", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });
        _s3.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });
        var sut = _CreateSut(supportsSourcePreconditions: false);

        var moved = await sut.MoveAsync(_Source, _Destination, AbortToken);

        moved.Should().BeTrue();
        await _s3.Received(1)
            .DeleteObjectAsync(
                Arg.Is<DeleteObjectRequest>(request => request.Key == "big/source.bin" && request.IfMatch == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(5L * 1024 * 1024)]
    [InlineData((5L * 1024 * 1024) + 1)]
    [InlineData(6L * 1024 * 1024 * 1024)]
    [InlineData(5L * 1024 * 1024 * 1024 * 1024)]
    public void part_ranges_cover_the_source_within_s3_part_limits(long size)
    {
        var ranges = AwsBlobStorage.ComputeCopyPartRanges(size);

        ranges.Count.Should().BeInRange(1, 10_000);
        ranges[0].FirstByte.Should().Be(0);
        ranges[^1].LastByte.Should().Be(size - 1);

        for (var i = 0; i < ranges.Count; i++)
        {
            var length = ranges[i].LastByte - ranges[i].FirstByte + 1;
            length.Should().BeLessThanOrEqualTo(5 * _GiB);

            if (i < ranges.Count - 1)
            {
                // Every part but the last must reach the 5 MiB S3 minimum, and the next part starts right after.
                length.Should().BeGreaterThanOrEqualTo(5 * _MiB);
                ranges[i + 1].FirstByte.Should().Be(ranges[i].LastByte + 1);
            }
        }
    }
}
