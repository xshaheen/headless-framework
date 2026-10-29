// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Probes;

/// <summary>
/// Probes the server behavior the S3 tus store is designed around, straight through the AWS SDK: conditional object
/// writes as a fencing token, completing a multipart upload from a subset of its parts, and copying a byte range of
/// an existing object into a part. A failing probe means the backend cannot host the store, not that the store is
/// wrong. Each test gets its own bucket and removes it afterwards.
/// </summary>
public abstract class S3CapabilityProbeTestsBase : TestBase
{
    /// <summary>The provider's minimum size for every part but the last.</summary>
    protected const int MinPartSize = 5 * 1024 * 1024;

    private S3TestClient? _client;
    private string? _bucket;

    /// <summary>Creates the client for the backend under test, or skips the test when the backend is unavailable.</summary>
    protected abstract S3TestClient CreateClient();

    /// <summary>
    /// How the backend rejects an <c>If-Match</c> write to a key that does not exist. AWS, MinIO and LocalStack answer
    /// 404 <c>NoSuchKey</c>; Cloudflare R2 answers 412. The store treats both as a stale write, so each backend pins
    /// its own shape here to catch a change in it.
    /// </summary>
    protected virtual (HttpStatusCode StatusCode, string ErrorCode) MissingKeyIfMatchFailure =>
        (HttpStatusCode.NotFound, "NoSuchKey");

    protected IAmazonS3 S3 => _client!.S3;

    protected string Bucket => _bucket!;

    public virtual async Task should_create_only_when_key_is_absent_with_if_none_match()
    {
        // given
        await StartAsync();
        var key = _NewKey();
        await PutAsync(key, "first"u8.ToArray(), ifNoneMatch: "*");

        // when
        var act = () => PutAsync(key, "second"u8.ToArray(), ifNoneMatch: "*");

        // then
        (await act.Should().ThrowAsync<AmazonS3Exception>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.PreconditionFailed);
        (await GetAsync(key)).Should().Equal("first"u8.ToArray());
    }

    public virtual async Task should_replace_only_the_etag_a_writer_read_with_if_match()
    {
        // given
        await StartAsync();
        var key = _NewKey();
        var firstEtag = await PutAsync(key, "first"u8.ToArray(), ifNoneMatch: "*");
        var secondEtag = await PutAsync(key, "second"u8.ToArray(), ifMatch: firstEtag);

        // when - a writer still holding the first ETag is stale
        var act = () => PutAsync(key, "stale"u8.ToArray(), ifMatch: firstEtag);

        // then
        secondEtag.Should().NotBe(firstEtag);
        (await act.Should().ThrowAsync<AmazonS3Exception>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.PreconditionFailed);
        (await GetAsync(key)).Should().Equal("second"u8.ToArray());
    }

    public virtual async Task should_reject_an_if_match_write_to_a_missing_key()
    {
        // given
        await StartAsync();
        var key = _NewKey();
        var etagOfAnotherObject = await PutAsync(_NewKey(), "other"u8.ToArray());

        // when
        var act = () => PutAsync(key, "data"u8.ToArray(), ifMatch: etagOfAnotherObject);

        // then
        var exception = (await act.Should().ThrowAsync<AmazonS3Exception>()).Which;
        exception.StatusCode.Should().Be(MissingKeyIfMatchFailure.StatusCode);
        exception.ErrorCode.Should().Be(MissingKeyIfMatchFailure.ErrorCode);
    }

    public virtual async Task should_complete_a_multipart_upload_from_a_subset_of_its_parts()
    {
        // given - three uploaded parts, of which the store would commit only the first and the third
        await StartAsync();
        var key = _NewKey();
        var parts = Enumerable.Range(1, 3).Select(i => RandomBytes(MinPartSize, seed: i)).ToArray();
        var uploadId = await InitiateAsync(key);
        var etags = new List<PartETag>();

        for (var i = 0; i < parts.Length; i++)
        {
            etags.Add(new PartETag(i + 1, await UploadPartAsync(key, uploadId, i + 1, parts[i])));
        }

        // when
        await S3.CompleteMultipartUploadAsync(
            new CompleteMultipartUploadRequest
            {
                BucketName = Bucket,
                Key = key,
                UploadId = uploadId,
                PartETags = [etags[0], etags[2]],
            },
            AbortToken
        );

        // then
        (await GetAsync(key))
            .Should()
            .Equal([.. parts[0], .. parts[2]]);
    }

    public virtual async Task should_copy_a_byte_range_of_an_object_into_a_part()
    {
        // given - a source larger than one part, and a range inside it that is not aligned to its start
        await StartAsync();
        var sourceKey = _NewKey();
        var source = RandomBytes(MinPartSize + (1024 * 1024), seed: 7);
        await PutAsync(sourceKey, source);

        const int rangeStart = 512 * 1024;
        var tail = RandomBytes(1024, seed: 8);
        var key = _NewKey();
        var uploadId = await InitiateAsync(key);

        // when
        var copy = await S3.CopyPartAsync(
            new CopyPartRequest
            {
                SourceBucket = Bucket,
                SourceKey = sourceKey,
                DestinationBucket = Bucket,
                DestinationKey = key,
                UploadId = uploadId,
                PartNumber = 1,
                FirstByte = rangeStart,
                LastByte = rangeStart + MinPartSize - 1,
            },
            AbortToken
        );
        var tailEtag = await UploadPartAsync(key, uploadId, 2, tail);

        await S3.CompleteMultipartUploadAsync(
            new CompleteMultipartUploadRequest
            {
                BucketName = Bucket,
                Key = key,
                UploadId = uploadId,
                PartETags = [new PartETag(1, copy.ETag), new PartETag(2, tailEtag)],
            },
            AbortToken
        );

        // then
        (await GetAsync(key))
            .Should()
            .Equal([.. source.AsSpan(rangeStart, MinPartSize), .. tail]);
    }

    /// <summary>Creates the client and the test's own bucket.</summary>
    protected async Task StartAsync()
    {
        _client = CreateClient();
        var bucket = $"tus-probe-{Guid.NewGuid():N}";

        await S3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, AbortToken);
        _bucket = bucket;
    }

    protected async Task<string> PutAsync(
        string key,
        byte[] content,
        string? ifNoneMatch = null,
        string? ifMatch = null
    )
    {
        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = new MemoryStream(content),
            IfNoneMatch = ifNoneMatch,
            IfMatch = ifMatch,
        };
        // The probes send writes exactly the way the store does, so a provider preset that the server rejects
        // fails here.
        TusS3RequestPresets.Apply(request, _client!.Provider);

        var response = await S3.PutObjectAsync(request, AbortToken);

        return response.ETag;
    }

    protected async Task<string> InitiateAsync(string key)
    {
        var response = await S3.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = Bucket, Key = key },
            AbortToken
        );

        return response.UploadId;
    }

    protected async Task<string> UploadPartAsync(string key, string uploadId, int partNumber, byte[] content)
    {
        var request = new UploadPartRequest
        {
            BucketName = Bucket,
            Key = key,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = new MemoryStream(content),
            PartSize = content.Length,
        };
        TusS3RequestPresets.Apply(request, _client!.Provider);

        var response = await S3.UploadPartAsync(request, AbortToken);

        return response.ETag;
    }

    protected async Task<byte[]> GetAsync(string key)
    {
        using var response = await S3.GetObjectAsync(Bucket, key, AbortToken);
        await using var content = new MemoryStream();
        await response.ResponseStream.CopyToAsync(content, AbortToken);

        return content.ToArray();
    }

    protected static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);

        return bytes;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_client is not null)
        {
            if (_bucket is not null)
            {
                await _DeleteBucketAsync();
            }

            _client.Dispose();
        }

        await base.DisposeAsyncCore();
    }

    private static string _NewKey()
    {
        return $"probe/{Guid.NewGuid():N}";
    }

    // Aborts what a failed probe left open, empties the bucket, and drops it, so a real account keeps no probe state.
    private async Task _DeleteBucketAsync()
    {
        var uploads = await S3.ListMultipartUploadsAsync(
            new ListMultipartUploadsRequest { BucketName = Bucket },
            CancellationToken.None
        );

        foreach (var upload in uploads.MultipartUploads ?? [])
        {
            await S3.AbortMultipartUploadAsync(Bucket, upload.Key, upload.UploadId, CancellationToken.None);
        }

        var objects = await S3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = Bucket },
            CancellationToken.None
        );

        foreach (var s3Object in objects.S3Objects ?? [])
        {
            await S3.DeleteObjectAsync(Bucket, s3Object.Key, CancellationToken.None);
        }

        await S3.DeleteBucketAsync(Bucket, CancellationToken.None);
    }
}
