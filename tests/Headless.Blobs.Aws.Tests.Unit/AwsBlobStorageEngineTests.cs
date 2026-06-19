// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Abstractions;
using Headless.Blobs.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AwsBlobStorageEngineTests : TestBase
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private AwsBlobStorage _CreateSut(AwsBlobStorageOptions? options = null)
    {
        var wrapper = new OptionsWrapper<AwsBlobStorageOptions>(options ?? new AwsBlobStorageOptions());

        return new AwsBlobStorage(_s3, new MimeTypeProvider(), new Clock(TimeProvider.System), wrapper);
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
        _s3.ReceivedCalls().Count().Should().Be(callsAfterFirst);
    }
}
