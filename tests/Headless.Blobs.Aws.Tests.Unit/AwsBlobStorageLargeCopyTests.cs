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
/// A copy of a source above 5 GiB, the largest one S3 accepts as a single <c>CopyObject</c>, at the real S3 limit.
/// </summary>
public sealed class AwsBlobStorageLargeCopyTests : TestBase
{
    private const long _SourceSize = 6L * 1024 * 1024 * 1024;

    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    [Fact]
    public async Task copy_copies_a_source_above_five_gib_with_a_multipart_copy()
    {
        // given: a 6 GiB source, and an S3 that refuses one CopyObject above 5 GiB.
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GetObjectMetadataResponse
                {
                    HttpStatusCode = HttpStatusCode.OK,
                    ContentLength = _SourceSize,
                    ETag = "\"source-etag\"",
                }
            );
        _s3.CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new AmazonS3Exception(
                    "The specified copy source is larger than the maximum allowable size for a copy source: 5368709120"
                )
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "InvalidRequest",
                }
            );
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

        await using var sut = new AwsBlobStorage(
            _s3,
            new MimeTypeProvider(),
            TimeProvider.System,
            new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions()),
            new AwsBlobNamingNormalizer()
        );

        // when
        var copied = await sut.CopyAsync(
            new BlobLocation("bucket", "big/source.bin"),
            new BlobLocation("bucket", "big/target.bin"),
            AbortToken
        );

        // then: 6 GiB in 5 MiB parts is 1,229 part copies, completed in ascending part order.
        copied.Should().BeTrue();
        await _s3.DidNotReceiveWithAnyArgs().CopyObjectAsync(default!, AbortToken);

        var parts = _s3.ReceivedCalls()
            .Where(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(IAmazonS3.CopyPartAsync), StringComparison.Ordinal)
            )
            .Select(call => (CopyPartRequest)call.GetArguments()[0]!)
            .OrderBy(request => request.PartNumber)
            .ToList();

        parts.Should().HaveCount(1229);
        parts[0].FirstByte.Should().Be(0);
        parts[^1].LastByte.Should().Be(_SourceSize - 1);

        await _s3.Received(1)
            .CompleteMultipartUploadAsync(
                Arg.Is<CompleteMultipartUploadRequest>(request =>
                    request.UploadId == "upload-1"
                    && request.PartETags.Select(part => part.PartNumber ?? 0).SequenceEqual(Enumerable.Range(1, 1229))
                ),
                Arg.Any<CancellationToken>()
            );
        await _s3.DidNotReceiveWithAnyArgs().AbortMultipartUploadAsync(default!, AbortToken);
    }
}
