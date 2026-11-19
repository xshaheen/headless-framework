// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Copy and move above the single-copy threshold, lowered so a 12 MiB source takes the multipart copy: the real
/// server must assemble the <c>UploadPartCopy</c> ranges into the same bytes and keep the source's metadata and
/// content type, which a multipart upload does not carry over on its own.
/// </summary>
public abstract class S3MultipartCopyTestsBase : TestBase
{
    private const int _SourceSize = 12 * 1024 * 1024;

    protected abstract AmazonS3Client CreateClient();

    private AwsBlobStorage _CreateStorage(AmazonS3Client client)
    {
        return new AwsBlobStorage(
            client,
            new MimeTypeProvider(),
            TimeProvider.System,
            new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions()),
            new AwsBlobNamingNormalizer()
        )
        {
            MultipartCopyThreshold = 1024 * 1024,
        };
    }

    private static byte[] _Content()
    {
        // Deterministic non-zero bytes, so a part copied from the wrong range changes the result.
        var data = new byte[_SourceSize];

        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        return data;
    }

    private async Task<(AwsBlobStorage Storage, AmazonS3Client Client, string Bucket)> _ArrangeAsync()
    {
#pragma warning disable CA2000 // Disposed by the AwsBlobStorage that owns it; the raw client below is the test's.
        var storage = _CreateStorage(CreateClient());
#pragma warning restore CA2000
        var client = CreateClient();
        var bucket = $"mpc-{Guid.NewGuid():N}";
        await client.PutBucketAsync(bucket, AbortToken);

        await using var content = new MemoryStream(_Content());
        await storage.UploadAsync(
            new BlobLocation(bucket, "source/large.bin"),
            content,
            "application/x-custom",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["owner"] = "alice" },
            AbortToken
        );

        return (storage, client, bucket);
    }

    private async Task<byte[]> _ReadAsync(IAmazonS3 client, string bucket, string key)
    {
        using var response = await client.GetObjectAsync(bucket, key, AbortToken);
        await using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, AbortToken);

        return buffer.ToArray();
    }

    [Fact]
    public async Task copy_above_the_threshold_keeps_bytes_metadata_and_content_type()
    {
        // given
        var (storage, client, bucket) = await _ArrangeAsync();
        await using var _ = storage;
        using var __ = client;
        var sourceInfo = await client.GetObjectMetadataAsync(bucket, "source/large.bin", AbortToken);

        // when
        var copied = await storage.CopyAsync(
            new BlobLocation(bucket, "source/large.bin"),
            new BlobLocation(bucket, "target/large.bin"),
            AbortToken
        );

        // then: the destination is a three-part multipart object (5 + 5 + 2 MiB) with the source's bytes.
        copied.Should().BeTrue();

        var target = await client.GetObjectMetadataAsync(bucket, "target/large.bin", AbortToken);
        target.ETag.Should().EndWith("-3\"");
        target.Headers.ContentType.Should().Be("application/x-custom");
        target.Headers.CacheControl.Should().Be(sourceInfo.Headers.CacheControl);
        target.Metadata["owner"].Should().Be("alice");
        target.Metadata["uploaddate"].Should().Be(sourceInfo.Metadata["uploaddate"]);
        (await _ReadAsync(client, bucket, "target/large.bin")).Should().Equal(_Content());

        var info = await storage.GetBlobInfoAsync(new BlobLocation(bucket, "target/large.bin"), AbortToken);
        info!.Metadata.Should().Contain("owner", "alice");

        // The completed copy leaves no incomplete upload billed on the bucket.
        var uploads = await client.ListMultipartUploadsAsync(bucket, AbortToken);
        (uploads.MultipartUploads ?? []).Should().BeEmpty();
    }

    [Fact]
    public async Task move_above_the_threshold_copies_then_deletes_the_source()
    {
        // given
        var (storage, client, bucket) = await _ArrangeAsync();
        await using var _ = storage;
        using var __ = client;

        // when
        var moved = await storage.MoveAsync(
            new BlobLocation(bucket, "source/large.bin"),
            new BlobLocation(bucket, "target/large.bin"),
            AbortToken
        );

        // then
        moved.Should().BeTrue();
        (await storage.ExistsAsync(new BlobLocation(bucket, "source/large.bin"), AbortToken)).Should().BeFalse();

        var target = await client.GetObjectMetadataAsync(bucket, "target/large.bin", AbortToken);
        target.Headers.ContentType.Should().Be("application/x-custom");
        target.Metadata["owner"].Should().Be("alice");
        (await _ReadAsync(client, bucket, "target/large.bin")).Should().Equal(_Content());
    }

    [Fact]
    public async Task copy_returns_false_when_the_source_is_missing()
    {
        // given
        var (storage, client, bucket) = await _ArrangeAsync();
        await using var _ = storage;
        using var __ = client;

        // when
        var copied = await storage.CopyAsync(
            new BlobLocation(bucket, "source/missing.bin"),
            new BlobLocation(bucket, "target/missing.bin"),
            AbortToken
        );

        // then
        copied.Should().BeFalse();
    }
}

/// <summary>The multipart copy against LocalStack's S3 emulator.</summary>
[Collection<AwsBlobStorageFixture>]
public sealed class AwsBlobStorageMultipartCopyTests(AwsBlobStorageFixture fixture) : S3MultipartCopyTestsBase
{
    protected override AmazonS3Client CreateClient()
    {
        return new AmazonS3Client(
            new BasicAWSCredentials("xxx", "xxx"),
            new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.USEast1,
                ServiceURL = fixture.Container.GetConnectionString(),
                ForcePathStyle = true,
            }
        );
    }
}

/// <summary>
/// The multipart copy against MinIO, which enforces the 5 MiB minimum part size and the per-part ETag precondition.
/// </summary>
[Collection<MinioFixture>]
public sealed class MinioMultipartCopyTests(MinioFixture fixture) : S3MultipartCopyTestsBase
{
    protected override AmazonS3Client CreateClient()
    {
        return new AmazonS3Client(
            new BasicAWSCredentials(MinioFixture.AccessKey, MinioFixture.SecretKey),
            new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.USEast1,
                ServiceURL = fixture.ConnectionString,
                ForcePathStyle = true,
            }
        );
    }

    [Fact]
    public async Task copy_aborts_when_the_source_is_overwritten_mid_copy()
    {
        // given: a 12 MiB source, and a client that overwrites it right before the first part copy is sent.
        using var client = CreateClient();
        var bucket = $"mpc-{Guid.NewGuid():N}";
        await client.PutBucketAsync(bucket, AbortToken);
        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = "source/large.bin",
                InputStream = new MemoryStream(new byte[12 * 1024 * 1024]),
            },
            AbortToken
        );

#pragma warning disable CA2000 // Disposed by the AwsBlobStorage that owns it.
        var overwriting = OverwriteBeforeFirstCopyPart.Wrap(CreateClient(), bucket, "source/large.bin");
#pragma warning restore CA2000
        await using var storage = new AwsBlobStorage(
            overwriting,
            new MimeTypeProvider(),
            TimeProvider.System,
            new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions()),
            new AwsBlobNamingNormalizer()
        )
        {
            MultipartCopyThreshold = 1024 * 1024,
        };

        // when
        var act = async () =>
            await storage.CopyAsync(
                new BlobLocation(bucket, "source/large.bin"),
                new BlobLocation(bucket, "target/large.bin"),
                AbortToken
            );

        // then: the pinned part fails its precondition, the upload is aborted, and no mixed destination exists.
        (await act.Should().ThrowAsync<AmazonS3Exception>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.PreconditionFailed);
        (await storage.ExistsAsync(new BlobLocation(bucket, "target/large.bin"), AbortToken)).Should().BeFalse();

        var uploads = await client.ListMultipartUploadsAsync(bucket, AbortToken);
        (uploads.MultipartUploads ?? []).Should().BeEmpty();
    }

    /// <summary>Forwards every call to a real client, overwriting one object just before the first part copy.</summary>
    public class OverwriteBeforeFirstCopyPart : DispatchProxy
    {
        private IAmazonS3 _inner = null!;
        private string _bucket = null!;
        private string _key = null!;
        private int _overwritten;

        public static IAmazonS3 Wrap(IAmazonS3 inner, string bucket, string key)
        {
            var proxy = Create<IAmazonS3, OverwriteBeforeFirstCopyPart>();
            var self = (OverwriteBeforeFirstCopyPart)(object)proxy;
            self._inner = inner;
            self._bucket = bucket;
            self._key = key;

            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (
                string.Equals(targetMethod?.Name, nameof(IAmazonS3.CopyPartAsync), StringComparison.Ordinal)
                && Interlocked.Exchange(ref _overwritten, 1) == 0
            )
            {
                return _CopyPartAfterOverwriteAsync((CopyPartRequest)args![0]!, (CancellationToken)args[1]!);
            }

            try
            {
                return targetMethod!.Invoke(_inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                ExceptionDispatchInfo.Throw(e.InnerException);

                throw;
            }
        }

        private async Task<CopyPartResponse> _CopyPartAfterOverwriteAsync(
            CopyPartRequest request,
            CancellationToken cancellationToken
        )
        {
            await _inner.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = _key,
                    InputStream = new MemoryStream(new byte[(12 * 1024 * 1024) + 1]),
                },
                cancellationToken
            );

            return await _inner.CopyPartAsync(request, cancellationToken);
        }
    }
}
